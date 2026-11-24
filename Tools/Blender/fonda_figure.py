"""La Fonda de la Abuela - chibi customer generator (Blender, headless).

Run:
    blender -b --factory-startup -P Tools/Blender/fonda_figure.py -- --type test --out <dir>

The script is the source of truth for every character mesh. Never hand-edit a .blend.
See Tools/Blender/README.md for all CLI options.

Conventions
- Part numbers are written in "figure units" on the sketch axes (three.js: x right, y up,
  z toward the face). They are converted to Blender as blender(x, y, z) = sketch(x, -z, y)
  and scaled by size * GLOBAL_SCALE, so characters look toward -Y.
- Every block is a cube with a bevel applied (bmesh), flat shaded, UVs collapsed to the
  centre of its colour swatch in Fonda_Palette.png.
"""

import argparse
import json
import math
import os
import struct
import sys
import zlib

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

GLOBAL_SCALE = 1.55
PALETTE_GRID = 16
PALETTE_CELL = 16
PALETTE_SIZE = PALETTE_GRID * PALETTE_CELL  # 256 px
MATERIAL_NAME = "Fonda_Characters"
PALETTE_NAME = "Fonda_Palette.png"

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
DEFAULT_OUT = os.path.join(REPO_ROOT, "Assets", "Art", "Characters")
DEFAULT_PREVIEWS = os.path.join(HERE, "previews")

# Bevel segments: 3 on head and torso (spec), 1 on limbs and small details (triangle budget).
HEAD_SEG = 3
LIMB_SEG = 1

INK = "#26262E"
MOUTH = "#7A2E2E"
CHEEKS = "#E98A7A"

# Sketch axes -> Blender axes: (x, y, z) -> (x, -z, y)
SKETCH_TO_BLENDER = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


# ---------------------------------------------------------------------------
# Colours and palette
# ---------------------------------------------------------------------------

def norm_hex(h):
    h = h.strip().upper()
    if not h.startswith("#"):
        h = "#" + h
    assert len(h) == 7, h
    return h


def hex_to_rgb(h):
    h = norm_hex(h)
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def darken(h, f):
    r, g, b = hex_to_rgb(h)
    return "#%02X%02X%02X" % (round(r * f), round(g * f), round(b * f))


class Palette:
    """Fixed, sorted list of unique hex colours -> 16 x 16 grid of 16 px swatches."""

    def __init__(self, colours):
        self.colours = sorted({norm_hex(c) for c in colours})
        if len(self.colours) > PALETTE_GRID * PALETTE_GRID:
            raise ValueError("Too many colours for the palette: %d" % len(self.colours))
        self.index = {c: i for i, c in enumerate(self.colours)}

    def cell(self, colour):
        """(column, row from the top of the PNG)"""
        i = self.index[norm_hex(colour)]
        return i % PALETTE_GRID, i // PALETTE_GRID

    def uv(self, colour):
        col, row = self.cell(colour)
        u = (col + 0.5) / PALETTE_GRID
        v = 1.0 - (row + 0.5) / PALETTE_GRID  # UV v=0 is the bottom of the image
        return u, v

    def write_png(self, path):
        """Write the palette as an 8-bit sRGB RGB PNG (pure Python, byte-stable)."""
        empty = (255, 0, 255)  # unused cells: magenta, easy to spot if a UV is wrong
        rows = []
        for py in range(PALETTE_SIZE):
            row = bytearray([0])  # filter type 0
            for px in range(PALETTE_SIZE):
                i = (py // PALETTE_CELL) * PALETTE_GRID + (px // PALETTE_CELL)
                rgb = hex_to_rgb(self.colours[i]) if i < len(self.colours) else empty
                row += bytes(rgb)
            rows.append(bytes(row))
        raw = b"".join(rows)

        def chunk(tag, data):
            c = struct.pack(">I", len(data)) + tag + data
            return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

        png = b"\x89PNG\r\n\x1a\n"
        png += chunk(b"IHDR", struct.pack(">IIBBBBB", PALETTE_SIZE, PALETTE_SIZE, 8, 2, 0, 0, 0))
        png += chunk(b"sRGB", b"\x00")
        png += chunk(b"IDAT", zlib.compress(raw, 9))
        png += chunk(b"IEND", b"")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(png)


# ---------------------------------------------------------------------------
# Mesh building (bmesh, sketch space)
# ---------------------------------------------------------------------------

def make_cylinder(r_top, r_bot, h, sides):
    tmp = bmesh.new()
    bot = [tmp.verts.new((r_bot * math.cos(2 * math.pi * k / sides), -h / 2,
                          -r_bot * math.sin(2 * math.pi * k / sides))) for k in range(sides)]
    top = [tmp.verts.new((r_top * math.cos(2 * math.pi * k / sides), h / 2,
                          -r_top * math.sin(2 * math.pi * k / sides))) for k in range(sides)]
    for k in range(sides):
        tmp.faces.new((bot[k], bot[(k + 1) % sides], top[(k + 1) % sides], top[k]))
    tmp.faces.new(top)
    tmp.faces.new(list(reversed(bot)))
    bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
    return tmp


def rot_matrix(rot):
    return Euler(rot, 'XYZ').to_matrix().to_4x4()


def tri_count_bm(bm):
    return sum(len(f.verts) - 2 for f in bm.faces)


class PartBuilder:
    """Collects parts (each in its own temporary bmesh) into one object mesh.

    Parts are given in local coordinates of `frame` (a 4x4 matrix into figure sketch
    space). Colour is applied by collapsing all UVs of the part to its swatch centre.
    """

    def __init__(self, palette, colours_used):
        self.palette = palette
        self.colours_used = colours_used
        self.bm = bmesh.new()
        self.uv_layer = self.bm.loops.layers.uv.new("UVMap")
        self.frame = Matrix.Identity(4)

    # -- primitives -------------------------------------------------------
    def box(self, size, pos, colour, radius=None, seg=1, rot=(0, 0, 0)):
        sx, sy, sz = size
        tmp = bmesh.new()
        bmesh.ops.create_cube(tmp, size=1.0)
        for v in tmp.verts:
            v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz))
        if radius is None:
            radius = min(0.03, 0.3 * min(size))
        radius = min(radius, 0.499 * min(size))
        if radius > 0 and seg > 0:
            bmesh.ops.bevel(
                tmp, geom=list(tmp.edges), offset=radius, offset_type='OFFSET',
                profile_type='SUPERELLIPSE', segments=seg, profile=0.5, affect='EDGES',
                clamp_overlap=True, loop_slide=True, mark_seam=False, mark_sharp=False,
                harden_normals=False, face_strength_mode='NONE', miter_outer='SHARP',
                miter_inner='SHARP', spread=0.1, vmesh_method='ADJ')
            bmesh.ops.remove_doubles(tmp, verts=tmp.verts, dist=1e-6)
        self._commit(tmp, Matrix.Translation(pos) @ rot_matrix(rot), colour)

    def cylinder(self, r_top, r_bot, h, pos, colour, sides=10, rot=(0, 0, 0)):
        """Cylinder along local Y (sketch up), like three.js CylinderGeometry.
        Vertices use the same angular phase as sphere(), so a band and a dome with the same
        number of sides line up and the band fully covers the dome."""
        self._commit(make_cylinder(r_top, r_bot, h, sides), Matrix.Translation(pos) @ rot_matrix(rot), colour)

    def slab(self, width, height, depth, radius, pos, colour, corner_segs=3):
        """Horizontal band whose outline (XZ) is a rounded rectangle, extruded along Y.
        Used for stripes that must hug the rounded torso."""
        tmp = bmesh.new()
        hx, hz = width / 2 - radius, depth / 2 - radius
        outline = []
        for cx, cz, a0 in ((hx, hz, 0.0), (-hx, hz, 0.5 * math.pi), (-hx, -hz, math.pi), (hx, -hz, 1.5 * math.pi)):
            for i in range(corner_segs + 1):
                a = a0 + 0.5 * math.pi * i / corner_segs
                outline.append((cx + radius * math.cos(a), cz + radius * math.sin(a)))
        bot = [tmp.verts.new((x, -height / 2, z)) for x, z in outline]
        top = [tmp.verts.new((x, height / 2, z)) for x, z in outline]
        n = len(outline)
        for k in range(n):
            tmp.faces.new((bot[k], bot[(k + 1) % n], top[(k + 1) % n], top[k]))
        tmp.faces.new(top)
        tmp.faces.new(list(reversed(bot)))
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        self._commit(tmp, Matrix.Translation(pos), colour)

    def sphere(self, r, pos, colour, squash=1.0, segs=8, rings=6, dome=False, rot=(0, 0, 0)):
        """Low-poly UV sphere (or upper half = dome with a flat bottom), along sketch Y."""
        tmp = bmesh.new()
        ring_list = []
        n_rings = rings // 2 if dome else rings
        for i in range(1, n_rings + (1 if dome else 0)):
            phi = (math.pi / 2) * i / n_rings if dome else math.pi * i / rings
            # phi measured from the top pole
            y = math.cos(phi) * r * squash
            rr = math.sin(phi) * r
            ring = [tmp.verts.new((rr * math.cos(2 * math.pi * k / segs), y,
                                   -rr * math.sin(2 * math.pi * k / segs))) for k in range(segs)]
            ring_list.append(ring)
        if dome:
            # last ring is at the equator (phi = pi/2)
            pass
        top = tmp.verts.new((0, r * squash, 0))
        for k in range(segs):
            tmp.faces.new((top, ring_list[0][k], ring_list[0][(k + 1) % segs]))
        for a, b in zip(ring_list, ring_list[1:]):
            for k in range(segs):
                tmp.faces.new((a[k], b[k], b[(k + 1) % segs], a[(k + 1) % segs]))
        if dome:
            tmp.faces.new(list(reversed(ring_list[-1])))
        else:
            bottom = tmp.verts.new((0, -r * squash, 0))
            last = ring_list[-1]
            for k in range(segs):
                tmp.faces.new((bottom, last[(k + 1) % segs], last[k]))
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        self._commit(tmp, Matrix.Translation(pos) @ rot_matrix(rot), colour)

    def torus_arc(self, big_r, tube_r, pos, colour, arc=math.pi, start=0.0, segs=8, sides=6,
                  rot=(0, 0, 0), caps=True):
        """Arc of a torus in the local XY plane, from angle `start` to `start + arc`."""
        tmp = bmesh.new()
        rings = []
        for i in range(segs + 1):
            a = start + arc * i / segs
            c, s = math.cos(a), math.sin(a)
            centre = Vector((big_r * c, big_r * s, 0))
            radial = Vector((c, s, 0))
            ring = []
            for k in range(sides):
                b = 2 * math.pi * k / sides
                p = centre + radial * (tube_r * math.cos(b)) + Vector((0, 0, tube_r * math.sin(b)))
                ring.append(tmp.verts.new(p))
            rings.append(ring)
        for a_ring, b_ring in zip(rings, rings[1:]):
            for k in range(sides):
                tmp.faces.new((a_ring[k], a_ring[(k + 1) % sides], b_ring[(k + 1) % sides], b_ring[k]))
        if caps:
            tmp.faces.new(rings[0])
            tmp.faces.new(list(reversed(rings[-1])))
        bmesh.ops.recalc_face_normals(tmp, faces=tmp.faces)
        self._commit(tmp, Matrix.Translation(pos) @ rot_matrix(rot), colour)

    def raw(self, tmp, colour, matrix=None):
        """Add an already built bmesh (local coordinates)."""
        self._commit(tmp, matrix or Matrix.Identity(4), colour)

    # -- internals --------------------------------------------------------
    def _commit(self, tmp, local, colour):
        colour = norm_hex(colour)
        self.colours_used.add(colour)
        m = self.frame @ local
        tmp.transform(m)
        if m.determinant() < 0:
            bmesh.ops.reverse_faces(tmp, faces=tmp.faces)
        uv = self.palette.uv(colour) if self.palette else (0.5, 0.5)
        vmap = {}
        for v in tmp.verts:
            vmap[v] = self.bm.verts.new(v.co)
        for f in tmp.faces:
            nf = self.bm.faces.new([vmap[v] for v in f.verts])
            for loop in nf.loops:
                loop[self.uv_layer].uv = uv
        tmp.free()

    def to_mesh(self, name, origin_sketch, scale):
        """Bake: sketch space -> Blender space, scaled, relative to origin (pivot)."""
        m = Matrix.Translation(-(SKETCH_TO_BLENDER @ Vector(origin_sketch)) * scale) \
            @ Matrix.Scale(scale, 4) @ SKETCH_TO_BLENDER
        self.bm.transform(m)
        self.bm.normal_update()
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for p in me.polygons:
            p.use_smooth = False
        return me


def _to_mesh_raw(self, name):
    self.bm.normal_update()
    me = bpy.data.meshes.new(name)
    self.bm.to_mesh(me)
    self.bm.free()
    for p in me.polygons:
        p.use_smooth = False
    return me


PartBuilder.to_mesh_raw = _to_mesh_raw


def to_blender(p, scale):
    return (SKETCH_TO_BLENDER @ Vector(p)) * scale


# ---------------------------------------------------------------------------
# Customer types
# ---------------------------------------------------------------------------

TEST_TYPE = {
    "size": 1.0, "wide": 1.0,
    "skin": "#E0AC7E", "hair": None, "hair_style": None,
    "shirt": "#3F4A5E", "trousers": "#2A3242", "shoes": "#3A2E28",
}

# The eight customer types. To add a type, add one entry here (see README.md).
# es = name used in the game (UI / design docs).
TYPES = {
    "Office": dict(
        es="Oficinista apurado", size=1.04, wide=1.00,
        skin="#E0AC7E", hair="#2B2522", hair_style="short",
        shirt="#3F4A5E", trousers="#2A3242", shoes="#1E1E24",
        clothing=["tie"], hand_r="briefcase", neutral_brows="worried"),
    "Grandpa": dict(
        es="Abuelito paciente", size=0.95, wide=1.00,
        skin="#F1C9A5", hair="#E6E6E6", hair_style="bald",
        shirt="#A0785A", trousers="#5B4A3E", shoes="#3A2E28",
        brows="#E6E6E6", mustache="#EDEDED", clothing=["buttons"],
        hat="boina", glasses="glasses", hand_l="cane"),
    "Child": dict(
        es="Niño", size=0.72, wide=1.00,
        skin="#8D5A3B", hair="#1E1A18", hair_style="short",
        shirt="#F08A24", trousers="#2F5DA8", shoes="#E84A5F",
        clothing=["stripes"], hat="cap", hat_colour="#2F5DA8", cheeks=True,
        neutral_mouth="grin"),
    "Regular": dict(
        es="Habitual (Don Pepe)", size=1.00, wide=1.15,
        skin="#C68A5A", hair="#3A2A20", hair_style="short",
        shirt="#3E7C59", trousers="#3B3B3B", shoes="#3A2E28",
        mustache="#3A2A20", clothing=["buttons"], hat="straw"),
    "Worker": dict(
        es="Trabajador de obra", size=1.06, wide=1.12,
        skin="#A86F48", hair="#2B2522", hair_style="short",
        shirt="#F2A33A", trousers="#3C5A80", shoes="#5A3A22",
        clothing=["hivis"], hat="helmet"),
    "Student": dict(
        es="Estudiante", size=1.00, wide=1.00,
        skin="#E8B98F", hair="#5A3A28", hair_style="fringe",
        shirt="#6B5BA8", trousers="#2E3A55", shoes="#F4F4F4",
        clothing=["backpack"], headphones=True),
    "Tourist": dict(
        es="Turista", size=1.00, wide=1.00,
        skin="#F6D2B8", hair="#E8C468", hair_style="bun",
        shirt="#2BA6A0", trousers="#E2D4B4", shoes="#8A5A3A",
        clothing=["floral", "camera"], glasses="sunglasses", hat="sunhat", shorts=True),
    "Stingy": dict(
        es="Tacaño", size=0.97, wide=1.00,
        skin="#D9A37A", hair="#4A3A30", hair_style="short",
        shirt="#6B4E3D", trousers="#3A2E28", shoes="#3A2E28",
        clothing=["buttons"], hand_r="purse", neutral_brows="frown", hat="beanie"),
}


def type_params(name):
    if name.lower() == "test":
        return "Test", dict(TEST_TYPE)
    for k, v in TYPES.items():
        if k.lower() == name.lower():
            return k, dict(v)
    raise SystemExit("Unknown type '%s'. Known: test, %s" % (name, ", ".join(TYPES)))


# ---------------------------------------------------------------------------
# Figure assembly
# ---------------------------------------------------------------------------

def new_empty(name, coll, location=(0, 0, 0), parent=None):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_type = 'PLAIN_AXES'
    ob.empty_display_size = 0.05
    coll.objects.link(ob)
    set_parent(ob, parent, location)
    return ob


def new_mesh_object(name, me, coll, location, parent, material):
    ob = bpy.data.objects.new(name, me)
    me.materials.append(material)
    coll.objects.link(ob)
    set_parent(ob, parent, location)
    return ob


def set_parent(ob, parent, world_location):
    """No rotation/scale anywhere: local location = world - parent world."""
    world_location = Vector(world_location)
    if parent is not None:
        ob.parent = parent
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.location = world_location - parent_world_location(parent)
    else:
        ob.location = world_location
    ob["pivot_world"] = list(world_location)


def parent_world_location(ob):
    return Vector(ob["pivot_world"])


def build_figure(type_name, p, palette, material, colours_used, coll=None, offset=(0, 0, 0)):
    """Build one customer. Returns the root empty."""
    W = p.get("wide", 1.0)
    S = p.get("size", 1.0) * GLOBAL_SCALE
    skin, shirt, trousers, shoes = p["skin"], p["shirt"], p["trousers"], p["shoes"]

    if coll is None:
        coll = bpy.data.collections.new("Customer_" + type_name)
        bpy.context.scene.collection.children.link(coll)

    def pb():
        return PartBuilder(palette, colours_used)

    root = new_empty("Customer_" + type_name, coll, offset)
    root["fonda_type"] = type_name
    root["size_scale"] = S
    off = Vector(offset)

    hips_s = (0, 0.17, 0)
    hips = new_empty("Hips", coll, off + to_blender(hips_s, S), root)

    # --- legs ---------------------------------------------------------------
    for side, sx in (("L", 1), ("R", -1)):
        pivot = (sx * 0.075 * W, 0.17, 0)
        b = pb()
        b.frame = Matrix.Translation(pivot)
        if p.get("shorts"):
            b.box((0.13, 0.07, 0.14), (0, 0.135 - 0.17, 0), trousers, seg=LIMB_SEG)
            b.box((0.11, 0.09, 0.12), (0, 0.075 - 0.17, 0), skin, seg=LIMB_SEG)
        else:
            b.box((0.13, 0.16, 0.14), (0, 0.09 - 0.17, 0), trousers, seg=LIMB_SEG)
        b.box((0.14, 0.07, 0.18), (0, 0.035 - 0.17, 0.02), shoes, radius=0.02, seg=LIMB_SEG)
        me = b.to_mesh("Leg_" + side, pivot, S)
        new_mesh_object("Leg_" + side, me, coll, off + to_blender(pivot, S), hips, material)

    # --- torso ------------------------------------------------------------
    b = pb()
    b.box((0.34 * W, 0.27, 0.23), (0, 0.29, 0), shirt, radius=0.07, seg=HEAD_SEG)
    b.box((0.35 * W, 0.065, 0.235), (0, 0.185, 0), trousers, radius=0.02, seg=LIMB_SEG)
    add_clothing(b, p, W)
    me = b.to_mesh("Torso", hips_s, S)
    torso = new_mesh_object("Torso", me, coll, off + to_blender(hips_s, S), hips, material)

    # --- arms -------------------------------------------------------------
    arms = {}
    for side, sx in (("L", 1), ("R", -1)):
        pivot = (sx * (0.17 * W + 0.045), 0.405, 0)
        tilt = Matrix.Translation(pivot) @ rot_matrix((0, 0, sx * 0.12))
        b = pb()
        b.frame = tilt
        b.box((0.09, 0.19, 0.10), (0, -0.09, 0), shirt, radius=0.03, seg=LIMB_SEG)
        b.box((0.095, 0.085, 0.10), (0, -0.215, 0), skin, radius=0.035, seg=LIMB_SEG)
        me = b.to_mesh("Arm_" + side, pivot, S)
        arms[side] = new_mesh_object("Arm_" + side, me, coll, off + to_blender(pivot, S), torso, material)
        add_handheld(arms[side], side, tilt, p, palette, material, colours_used, coll, off, S)

    # --- head -------------------------------------------------------------
    neck = (0, 0.42, 0)
    b = pb()
    b.box((0.44, 0.40, 0.38), (0, 0.62, 0), skin, radius=0.11, seg=HEAD_SEG)
    for sx in (1, -1):
        b.box((0.05, 0.08, 0.06), (sx * 0.235, 0.61, 0), skin, radius=0.015, seg=1)
        b.box((0.045, 0.07, 0.02), (sx * 0.09, 0.63, 0.19), INK, radius=0.008, seg=1)
    b.box((0.06, 0.05, 0.04), (0, 0.585, 0.195), darken(skin, 0.85), radius=0.015, seg=1)
    me = b.to_mesh("Head", neck, S)
    head = new_mesh_object("Head", me, coll, off + to_blender(neck, S), torso, material)
    add_head_parts(head, neck, p, palette, material, colours_used, coll, off, S)

    root["height_m"] = figure_height(root)
    return root


# ---------------------------------------------------------------------------
# Optional parts (Prompt 2): clothing, hand-held props, faces, hair, headwear, glasses
# ---------------------------------------------------------------------------
#
# A type switches parts on with keys in its dict (see TYPES):
#   hair_style: short | fringe | bald | bun | None      hair: colour
#   brows: colour (default: hair colour, ink if no hair)
#   mustache: colour | None                            cheeks: bool
#   hat: helmet | cap | boina | straw | sunhat | beanie | None   hat_colour: colour (cap)
#   headphones: bool                                   glasses: glasses | sunglasses | None
#   clothing: [tie, buttons, stripes, hivis, backpack, floral, camera]
#   hand_r: briefcase | purse | None                   hand_l: cane | None
#   neutral_brows: flat | worried | frown              neutral_mouth: line | grin
#   shorts: bool

HATS = ("helmet", "cap", "boina", "straw", "sunhat", "beanie")

PART_COLOURS = {
    "helmet": "#F5C518", "helmet_ridge": "#E0AE00", "cap": "#2F5DA8", "cap_button": "#F5C518",
    "boina": "#5E6B7A", "straw": "#D9B36A", "straw_band": "#7A3E2B",
    "sunhat": "#F3E6C4", "sunhat_band": "#E0607E",
    "beanie": "#8B3A3A", "beanie_cuff": "#A44A4A", "beanie_pompom": "#E9DCC9",
    "headphones_band": "#2A2A2A", "headphones_cups": "#E84A5F",
    "lens": "#CFE3EE", "sunlens": "#1E1E26",
    "collar": "#F2F2F2", "tie": "#C0392B", "buttons": "#F4EDE1",
    "stripes": "#FFF1DC", "hivis": "#EAF0F4", "backpack": "#2B2742",
    "flower_1": "#F7D046", "flower_2": "#F28AB2", "flower_3": "#FFFFFF",
    "camera_strap": "#222222", "camera_body": "#2B2B2B", "camera_lens": "#8FB3D9",
    "briefcase": "#6B3E26", "purse": "#A67C52", "purse_clasp": "#E0A526", "cane": "#7A4E2D",
}

BROW_TILT = {"flat": 0.0, "worried": -0.25, "frown": 0.2, "angry": 0.35}

TORSO_Y, TORSO_HY, TORSO_HZ, TORSO_R = 0.29, 0.135, 0.115, 0.07


def torso_front_z(x, y, W):
    """z of the rounded torso's front surface at (x, y), so front details sit on it."""
    hx = 0.17 * W
    dx = max(0.0, abs(x) - (hx - TORSO_R))
    dy = max(0.0, abs(y - TORSO_Y) - (TORSO_HY - TORSO_R))
    d2 = dx * dx + dy * dy
    if d2 >= TORSO_R * TORSO_R:
        return TORSO_HZ - TORSO_R
    return (TORSO_HZ - TORSO_R) + math.sqrt(TORSO_R * TORSO_R - d2)


def front(x, y, W, thickness):
    """Centre z for a flat detail of `thickness` laid on the torso front (slightly sunk)."""
    return torso_front_z(x, y, W) + thickness / 2 - 0.004


def clothing_list(p):
    return list(p.get("clothing", []))


def add_clothing(b, p, W):
    c = PART_COLOURS
    for item in clothing_list(p):
        if item == "tie":
            b.box((0.15, 0.035, 0.02), (0, 0.41, front(0, 0.41, W, 0.02)), c["collar"], radius=0.008)
            b.box((0.05, 0.17, 0.02), (0, 0.32, front(0, 0.32, W, 0.02)), c["tie"], radius=0.008)
        elif item == "buttons":
            for y in (0.37, 0.31, 0.25):
                b.box((0.028, 0.028, 0.028), (0, y, front(0, y, W, 0.028)), c["buttons"], radius=0.008)
        elif item in ("stripes", "hivis"):
            hv = item == "hivis"
            # 0.345*W x h x 0.235 bands; outline follows the torso's rounded corners
            for y in ((0.25, 0.34) if hv else (0.27, 0.35)):
                b.slab(0.34 * W + 0.008, 0.04 if hv else 0.035, 0.23 + 0.008, TORSO_R + 0.004,
                       (0, y, 0), c[item], corner_segs=HEAD_SEG)
        elif item == "backpack":
            for sx in (1, -1):
                b.box((0.035, 0.24, 0.02), (sx * 0.085, 0.30, front(sx * 0.085, 0.30, W, 0.02)),
                      c["backpack"], radius=0.008)
            b.box((0.27, 0.23, 0.10), (0, 0.30, -0.16), c["backpack"], radius=0.03)
            b.box((0.30, 0.06, 0.09), (0, 0.43, -0.09), p["shirt"], radius=0.02)
        elif item == "floral":
            spots = ((-0.115, 0.345), (0.115, 0.33), (-0.085, 0.235), (0.095, 0.235), (0.0, 0.385))
            cols = (c["flower_1"], c["flower_2"], c["flower_3"], c["flower_1"], c["flower_2"])
            for (x, y), col in zip(spots, cols):
                b.box((0.035, 0.035, 0.012), (x * W, y, front(x * W, y, W, 0.012)), col, radius=0.006)
        elif item == "camera":
            for sx in (1, -1):
                b.box((0.02, 0.14, 0.012), (sx * 0.06, 0.36, front(sx * 0.06, 0.36, W, 0.012)),
                      c["camera_strap"], radius=0.004, rot=(0, 0, -sx * 0.5))
            b.box((0.11, 0.075, 0.05), (0, 0.29, 0.13), c["camera_body"], radius=0.012)
            b.cylinder(0.028, 0.028, 0.03, (0, 0.29, 0.13 + 0.025 + 0.012), c["camera_lens"],
                       sides=10, rot=(math.pi / 2, 0, 0))
        else:
            raise ValueError("unknown clothing item %s" % item)


def add_handheld(arm_ob, side, tilt, p, palette, material, colours_used, coll, off, S):
    c = PART_COLOURS
    prop = p.get("hand_r") if side == "R" else p.get("hand_l")
    if not prop:
        return None
    b = PartBuilder(palette, colours_used)
    b.frame = tilt
    if prop == "briefcase":
        b.box((0.07, 0.02, 0.02), (0, -0.24, 0.02), INK, radius=0.006)
        b.box((0.22, 0.16, 0.07), (0, -0.33, 0.02), c["briefcase"], radius=0.02)
    elif prop == "purse":
        b.box((0.11, 0.08, 0.05), (0, -0.29, 0.03), c["purse"], radius=0.015)
        b.sphere(0.015, (0, -0.25, 0.055), c["purse_clasp"], segs=6, rings=4)
    elif prop == "cane":
        b.cylinder(0.014, 0.014, 0.25, (0, -0.32, 0.05), c["cane"], sides=8)
        # hook: half torus over the hand, curving toward the face
        b.torus_arc(0.035, 0.014, (0, -0.195, 0.085), c["cane"], arc=math.pi, start=0.0,
                    segs=6, sides=6, rot=(0, -math.pi / 2, 0))
    else:
        raise ValueError("unknown hand-held prop %s" % prop)
    grip = tilt @ Vector((0, -0.215, 0))
    name = prop.capitalize()
    me = b.to_mesh(name, grip, S)
    return new_mesh_object(name, me, coll, off + to_blender(grip, S), arm_ob, material)


def build_face(b, p, variant):
    """variant: happy | neutral | angry"""
    brows_col = p.get("brows") or p.get("hair") or INK
    if variant == "happy":
        tilt = 0.0
    elif variant == "angry":
        tilt = BROW_TILT["angry"]
    else:
        tilt = BROW_TILT[p.get("neutral_brows", "flat")]
    for sx in (1, -1):
        b.box((0.08, 0.022, 0.02), (sx * 0.09, 0.695, 0.19), brows_col, radius=0.007, rot=(0, 0, sx * tilt))
    mouth = "grin" if variant == "happy" else ("down" if variant == "angry" else p.get("neutral_mouth", "line"))
    if mouth == "grin":
        b.box((0.09, 0.04, 0.02), (0, 0.53, 0.188), MOUTH, radius=0.012)
    elif mouth == "line":
        b.box((0.07, 0.016, 0.012), (0, 0.53, 0.19), MOUTH, radius=0.005)
    elif mouth == "down":
        half = 0.035
        for sx in (1, -1):
            a = -sx * 0.3  # outer ends down
            cx = sx * math.cos(a) * half / 2
            cy = 0.53 + 0.004 + math.sin(-abs(a)) * half / 2
            b.box((half + 0.004, 0.016, 0.012), (cx, cy, 0.19), MOUTH, radius=0.005, rot=(0, 0, a))
    if p.get("cheeks"):
        for sx in (1, -1):
            b.box((0.06, 0.03, 0.01), (sx * 0.14, 0.565, 0.19), CHEEKS, radius=0.004)
    if p.get("mustache"):
        b.box((0.16, 0.035, 0.03), (0, 0.552, 0.205), p["mustache"], radius=0.012)


def build_hair(b, p):
    style = p.get("hair_style")
    col = p.get("hair")
    if not style or not col:
        return False
    hat = p.get("hat")
    if style == "bald":
        for sx in (1, -1):
            b.box((0.05, 0.13, 0.24), (sx * 0.232, 0.65, -0.05), col, radius=0.015)
        b.box((0.42, 0.13, 0.06), (0, 0.63, -0.19), col, radius=0.02)
        return True
    if hat in HATS:
        # under any hat only the back and the sides show
        b.box((0.46, 0.20, 0.08), (0, 0.64, -0.17), col, radius=0.03)
        for sx in (1, -1):
            b.box((0.05, 0.10, 0.26), (sx * 0.225, 0.68, -0.04), col, radius=0.015)
        return True
    if style in ("short", "fringe"):
        b.box((0.46, 0.13, 0.40), (0, 0.795, 0), col, radius=0.045, seg=2)
    elif style == "bun":
        b.box((0.46, 0.12, 0.40), (0, 0.80, 0), col, radius=0.045, seg=2)
        b.sphere(0.08, (0, 0.86, -0.12), col, segs=8, rings=6)
    else:
        raise ValueError("unknown hair style %s" % style)
    b.box((0.46, 0.28, 0.08), (0, 0.68, -0.17), col, radius=0.03)
    for sx in (1, -1):
        b.box((0.05, 0.15, 0.30), (sx * 0.225, 0.72, -0.03), col, radius=0.015)
    if style == "fringe":
        b.box((0.42, 0.07, 0.07), (0, 0.765, 0.17), col, radius=0.02, rot=(-0.2, 0, 0))
    return True


def build_hat(b, p):
    c = PART_COLOURS
    hat = p.get("hat")
    if hat == "helmet":
        b.sphere(0.255, (0, 0.74, 0), c["helmet"], squash=0.8, segs=12, rings=6, dome=True)
        b.cylinder(0.30, 0.30, 0.03, (0, 0.745, 0), c["helmet"], sides=12)
        # ridge: the spec's 0.06 x 0.05 x 0.46 bar, bent to follow the dome so its ends
        # do not float (a straight bar at y 0.93 hangs ~7 cm above the dome's front/back)
        rb = bmesh.new()
        segs, w, t = 6, 0.06, 0.04
        rings = []
        for i in range(segs + 1):
            a = math.pi * i / segs
            ry, rz = math.sin(a), math.cos(a)
            ring = []
            for dx, dr in ((-w / 2, 0), (w / 2, 0), (w / 2, t), (-w / 2, t)):
                rr = 0.235 + dr
                ring.append(rb.verts.new((dx, ry * rr * 0.8, rz * rr)))
            rings.append(ring)
        for ra, rb2 in zip(rings, rings[1:]):
            for k in range(4):
                rb.faces.new((ra[k], ra[(k + 1) % 4], rb2[(k + 1) % 4], rb2[k]))
        rb.faces.new(rings[0])
        rb.faces.new(list(reversed(rings[-1])))
        bmesh.ops.recalc_face_normals(rb, faces=rb.faces)
        b.raw(rb, c["helmet_ridge"], Matrix.Translation((0, 0.74, 0)))
    elif hat == "cap":
        col = p.get("hat_colour") or c["cap"]
        b.sphere(0.24, (0, 0.76, 0), col, squash=0.75, segs=10, rings=6, dome=True)
        b.box((0.24, 0.025, 0.17), (0, 0.765, 0.25), col, radius=0.01)
        b.sphere(0.025, (0, 0.76 + 0.24 * 0.75 + 0.01, 0), c["cap_button"], segs=6, rings=4)
    elif hat == "boina":
        m = Matrix.Translation((0.02, 0.83, 0)) @ rot_matrix((0, 0, -0.12))
        b.raw(make_cylinder(0.24, 0.23, 0.07, 12), c["boina"], m)
        b.raw(_sph(0.025, 6, 4), c["boina"], m @ Matrix.Translation((0, 0.035 + 0.015, 0)))
    elif hat == "straw":
        b.cylinder(0.38, 0.38, 0.025, (0, 0.80, 0), c["straw"], sides=12)
        b.cylinder(0.19, 0.21, 0.15, (0, 0.885, 0), c["straw"], sides=10)
        b.cylinder(0.215, 0.215, 0.04, (0, 0.83, 0), c["straw_band"], sides=10)
    elif hat == "sunhat":
        b.cylinder(0.42, 0.42, 0.02, (0, 0.80, 0), c["sunhat"], sides=12)
        b.sphere(0.215, (0, 0.80, 0), c["sunhat"], segs=10, rings=6, dome=True)
        b.cylinder(0.218, 0.218, 0.045, (0, 0.825, 0), c["sunhat_band"], sides=10)
    elif hat == "beanie":
        b.sphere(0.245, (0, 0.74, 0), c["beanie"], squash=0.85, segs=10, rings=6, dome=True)
        b.cylinder(0.25, 0.25, 0.07, (0, 0.76, 0), c["beanie_cuff"], sides=10)
        b.sphere(0.06, (0, 0.97, 0), c["beanie_pompom"], segs=8, rings=4)
    elif hat:
        raise ValueError("unknown hat %s" % hat)
    return bool(hat)


def _sph(r, segs, rings):
    t = bmesh.new()
    bmesh.ops.create_uvsphere(t, u_segments=segs, v_segments=rings, radius=r)
    return t


def build_headphones(b, p):
    c = PART_COLOURS
    b.torus_arc(0.25, 0.022, (0, 0.63, 0), c["headphones_band"], arc=math.pi, start=0.0, segs=8, sides=6)
    for sx in (1, -1):
        b.box((0.06, 0.13, 0.13), (sx * 0.25, 0.63, 0), c["headphones_cups"], radius=0.02)


def build_glasses(b, p):
    kind = p.get("glasses")
    lens = PART_COLOURS["sunlens" if kind == "sunglasses" else "lens"]
    for sx in (1, -1):
        b.box((0.11, 0.09, 0.012), (sx * 0.09, 0.63, 0.20), INK, radius=0.006)
        b.box((0.085, 0.065, 0.012), (sx * 0.09, 0.63, 0.207), lens, radius=0.005)
        if kind == "glasses":
            # the palette material is opaque, so clear lenses would hide the eyes:
            # draw the pupils on the lens
            b.box((0.032, 0.05, 0.008), (sx * 0.09, 0.63, 0.215), INK, radius=0.004)
    b.box((0.05, 0.015, 0.012), (0, 0.645, 0.205), INK, radius=0.005)


FACE_VARIANTS = (("Face_Happy", "happy"), ("Face_Neutral", "neutral"), ("Face_Angry", "angry"))


def add_head_parts(head, neck, p, palette, material, colours_used, coll, off, S):
    loc = off + to_blender(neck, S)

    def part(name, fn):
        b = PartBuilder(palette, colours_used)
        if fn(b, p) is False:
            b.bm.free()
            return None
        me = b.to_mesh(name, neck, S)
        return new_mesh_object(name, me, coll, loc, head, material)

    for name, variant in FACE_VARIANTS:
        ob = part(name, lambda b, p, v=variant: build_face(b, p, v))
        visible = variant == "neutral"
        ob.hide_render = not visible
        ob.hide_viewport = not visible
    if p.get("hair_style"):
        part("Hair", build_hair)
    if p.get("hat"):
        part("Hat", build_hat)
    if p.get("headphones"):
        part("Headphones", build_headphones)
    if p.get("glasses"):
        part("Glasses", build_glasses)


def set_face(root, variant):
    """Show exactly one Face_* child (render previews). variant: Happy | Neutral | Angry"""
    for o in descendants(root):
        base = o.name.split(".")[0]
        if base.startswith("Face_"):
            vis = base == "Face_" + variant
            o.hide_render = not vis
            o.hide_viewport = not vis


# ---------------------------------------------------------------------------
# Queries
# ---------------------------------------------------------------------------

def descendants(ob):
    out = []
    for c in ob.children:
        out.append(c)
        out.extend(descendants(c))
    return out


def mesh_objects(root):
    return [o for o in [root] + descendants(root) if o.type == 'MESH']


def world_bbox(objs):
    bpy.context.view_layer.update()
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    return mn, mx


def figure_height(root):
    mn, mx = world_bbox(mesh_objects(root))
    return mx.z - root.matrix_world.translation.z


def triangle_count(root):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in mesh_objects(root))


def hierarchy(ob, depth=0):
    node = {"name": ob.name, "type": ob.type}
    if ob.type == 'MESH':
        node["triangles"] = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if ob.children:
        node["children"] = [hierarchy(c) for c in sorted(ob.children, key=lambda o: o.name)]
    return node


# ---------------------------------------------------------------------------
# Material
# ---------------------------------------------------------------------------

def make_material(palette_path):
    img = bpy.data.images.load(palette_path, check_existing=True)
    img.name = PALETTE_NAME
    img.colorspace_settings.name = 'sRGB'
    mat = bpy.data.materials.get(MATERIAL_NAME) or bpy.data.materials.new(MATERIAL_NAME)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 1.0
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.0
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = 'Closest'
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    nt.nodes.active = tex
    mat.diffuse_color = (1, 1, 1, 1)
    return mat


def flat_material(name, hex_colour):
    """Render-only material (stools, bar, grid). Never exported."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    r, g, b = (c / 255.0 for c in hex_to_rgb(hex_colour))
    lin = [((c + 0.055) / 1.055) ** 2.4 if c > 0.04045 else c / 12.92 for c in (r, g, b)]
    mat.diffuse_color = (*lin, 1.0)
    return mat


# ---------------------------------------------------------------------------
# Scene, rendering
# ---------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    sh = scene.display.shading
    sh.light = 'FLAT'
    sh.color_type = 'TEXTURE'
    sh.show_object_outline = True
    sh.object_outline_color = (0.08, 0.08, 0.1)
    scene.display_settings.display_device = 'sRGB'
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    world = bpy.data.worlds.new("World")
    world.color = (0.80, 0.78, 0.74)
    scene.world = world
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.use_overwrite = True
    return scene


def prop_box(name, size, centre, mat, coll=None):
    """Plain render-only box (Blender coords, metres)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0] + centre[0], v.co.y * size[1] + centre[1], v.co.z * size[2] + centre[2]))
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat)
    (coll or bpy.context.scene.collection).objects.link(ob)
    ob["render_prop"] = True
    return ob


def floor_grid(x0, x1, y0, y1, step=1.0, coll=None, line=0.01):
    mat = flat_material("Preview_Grid", "#8C877E")
    obs = []
    nx = int(round((x1 - x0) / step))
    ny = int(round((y1 - y0) / step))
    for i in range(nx + 1):
        x = x0 + i * step
        obs.append(prop_box("GridX", (line, y1 - y0, line / 5), (x, (y0 + y1) / 2, -line / 10), mat, coll))
    for j in range(ny + 1):
        y = y0 + j * step
        obs.append(prop_box("GridY", (x1 - x0, line, line / 5), ((x0 + x1) / 2, y, -line / 10), mat, coll))
    return obs


def camera(name, target, direction, ortho_scale, distance=10.0):
    cam_data = bpy.data.cameras.new(name)
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = ortho_scale
    cam_data.clip_end = 100
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.scene.collection.objects.link(cam)
    d = Vector(direction).normalized()
    cam.location = Vector(target) - d * distance
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    return cam


VIEWS = {
    # direction the camera looks in (Blender axes); the figure faces -Y
    "front": (0, 1, 0),
    "three_quarter": (-0.6, 0.8, -0.25),
    "side": (1, 0, 0),
}


def render(path, cam):
    scene = bpy.context.scene
    scene.camera = cam
    scene.render.filepath = path
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.render.render(write_still=True)
    log("render", os.path.relpath(path, REPO_ROOT))


def set_seated(root, seat_height=0.62):
    """Rotate both legs -90 deg on X at the hip and put the thighs on a seat."""
    S = root["size_scale"]
    for o in descendants(root):
        if o.name.split(".")[0] in ("Leg_L", "Leg_R"):
            o.rotation_euler = (-math.pi / 2, 0, 0)
    # thigh underside = hip height - half the trouser depth (0.07)
    lift = seat_height - (0.17 - 0.07) * S
    root.location.z = lift
    bpy.context.view_layer.update()
    return lift


def set_standing(root):
    for o in descendants(root):
        if o.name.split(".")[0] in ("Leg_L", "Leg_R"):
            o.rotation_euler = (0, 0, 0)
    root.location.z = 0
    bpy.context.view_layer.update()


def render_views(root, out_dir, prefix="", seated=True):
    """front / three-quarter / side / seated for one figure at the origin."""
    h = root["height_m"]
    target = Vector((0, 0, h * 0.5))
    scale = max(h, 0.9) * 1.25
    grid = floor_grid(-1, 1, -1, 1, 0.5)
    for view, d in VIEWS.items():
        cam = camera("Cam_" + view, target, d, scale)
        render(os.path.join(out_dir, prefix + view + ".png"), cam)
    if seated:
        wood = flat_material("Preview_Stool", "#8A5A3A")
        S = root["size_scale"]
        stool = prop_box("Stool", (0.36, 0.36, 0.62), (0, 0, 0.31), wood)
        lift = set_seated(root)
        mn, mx = world_bbox(mesh_objects(root))
        target = Vector((0, 0, (mx.z + 0) * 0.5))
        cam = camera("Cam_seated", target, (-0.75, 0.6, -0.2), max(mx.z, 1.0) * 1.2)
        render(os.path.join(out_dir, prefix + "seated.png"), cam)
        set_standing(root)
        bpy.data.objects.remove(stool)
    for g in grid:
        bpy.data.objects.remove(g)


def text_label(text, location, size=0.12, colour="#26262E", coll=None, rot=(math.pi / 2, 0, 0)):
    """Render-only label (Workbench draws text objects). Never exported."""
    cu = bpy.data.curves.new("Label", 'FONT')
    cu.body = text
    cu.align_x = 'CENTER'
    cu.size = size
    ob = bpy.data.objects.new("Label", cu)
    ob.location = location
    ob.rotation_euler = rot
    cu.materials.append(flat_material("Preview_Label_" + colour, colour))
    (coll or bpy.context.scene.collection).objects.link(ob)
    ob["render_prop"] = True
    return ob


def lineup_positions(widths, gap=0.25):
    xs, x = [], 0.0
    for w in widths:
        xs.append(x + w / 2)
        x += w + gap
    total = x - gap
    return [v - total / 2 for v in xs], total


def build_lineup(entries, palette, material, colours_used, gap=0.3, labels=True):
    """entries: list of (label, type_name, params). Figures stand side by side along X."""
    widths = [0.62 * GLOBAL_SCALE * e[2].get("size", 1.0) * max(1.0, e[2].get("wide", 1.0)) for e in entries]
    xs, total = lineup_positions(widths, gap)
    roots = []
    for (label, name, params), x in zip(entries, xs):
        r = build_figure(name, params, palette, material, colours_used, offset=(x, 0, 0))
        r["label"] = label
        roots.append(r)
        if labels:
            text_label(label, (x, -0.8, 0.0), size=0.13)
    return roots, total


def render_group(path, roots, total_width, direction=(0, 1, 0), height=None, extra=0.6):
    mn, mx = world_bbox([m for r in roots for m in mesh_objects(r)])
    top = height or mx.z
    grid = floor_grid(-math.ceil(total_width / 2 + 0.5), math.ceil(total_width / 2 + 0.5), -1, 1, 1.0)
    target = Vector(((mn.x + mx.x) / 2, 0, top / 2 - 0.1))
    scale = max(total_width + extra, top * 1.3)
    cam = camera("Cam_group", target, direction, scale, distance=15)
    scene = bpy.context.scene
    rx, ry = scene.render.resolution_x, scene.render.resolution_y
    aspect = min(3.0, max(1.0, (total_width + extra) / (top * 1.3)))
    scene.render.resolution_x = int(round(1024 * aspect))
    cam.data.sensor_fit = 'HORIZONTAL'
    cam.data.ortho_scale = max(total_width + extra, top * 1.3 * aspect)
    render(path, cam)
    scene.render.resolution_x, scene.render.resolution_y = rx, ry
    for g in grid:
        bpy.data.objects.remove(g)
    bpy.data.objects.remove(cam)


def run_parts(preview_dir):
    """Prompt 2 check: one line-up per part group."""
    reset_scene()
    pal = Palette(all_palette_colours() | {"#3A2A20"})
    pal_path = os.path.join(preview_dir, "parts", PALETTE_NAME)
    pal.write_png(pal_path)
    mat = make_material(pal_path)
    base = dict(TEST_TYPE, hair="#3A2A20", hair_style="short")
    groups = {
        "hair": [(s, "Hair_" + s, dict(base, hair_style=s)) for s in ("short", "fringe", "bald", "bun")]
                + [("short + hat", "Hair_under_hat", dict(base, hat="beanie"))],
        "hats": [(h, "Hat_" + h, dict(base, hat=h, hat_colour="#2F5DA8")) for h in HATS]
                + [("headphones", "Hat_headphones", dict(base, headphones=True))],
        "faces": [("Happy", "F_happy", dict(base)), ("Neutral", "F_neutral", dict(base)),
                  ("Angry", "F_angry", dict(base)),
                  ("worried", "F_worried", dict(base, neutral_brows="worried")),
                  ("frown", "F_frown", dict(base, neutral_brows="frown")),
                  ("grin+cheeks", "F_grin", dict(base, neutral_mouth="grin", cheeks=True)),
                  ("mustache", "F_must", dict(base, mustache="#3A2A20", brows="#3A2A20"))],
        "accessories": [("glasses", "A_glasses", dict(base, glasses="glasses")),
                        ("sunglasses", "A_sun", dict(base, glasses="sunglasses")),
                        ("tie+briefcase", "A_tie", dict(base, clothing=["tie"], hand_r="briefcase")),
                        ("buttons+purse", "A_btn", dict(base, clothing=["buttons"], hand_r="purse")),
                        ("stripes+cane", "A_str", dict(base, clothing=["stripes"], hand_l="cane")),
                        ("hivis", "A_hivis", dict(base, clothing=["hivis"])),
                        ("backpack", "A_pack", dict(base, clothing=["backpack"])),
                        ("floral+camera", "A_floral", dict(base, clothing=["floral", "camera"], shorts=True))],
    }
    report = {}
    for gname, entries in groups.items():
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o)
        used = set()
        roots, total = build_lineup(entries, pal, mat, used)
        if gname == "faces":
            for r, (label, _, _) in zip(roots, entries):
                if label in ("Happy", "Angry"):
                    set_face(r, label)
        tris = {r["label"]: triangle_count(r) for r in roots}
        report[gname] = tris
        log(gname, json.dumps(tris))
        d = (0, 1, 0) if gname == "faces" else (-0.45, 0.85, -0.2)
        render_group(os.path.join(preview_dir, "parts", gname + ".png"), roots, total, d)
        if gname == "accessories":
            render_group(os.path.join(preview_dir, "parts", gname + "_back.png"), roots, total, (0.45, -0.85, -0.2))
    # hierarchy check on one figure: faces are separate children of Head, only Neutral visible
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    r = build_figure("PartsCheck", dict(base, hat="cap", glasses="glasses", mustache="#3A2A20",
                                        cheeks=True), pal, mat, set())
    head = [o for o in descendants(r) if o.name == "Head"][0]
    faces = {o.name: {"render": not o.hide_render, "viewport": not o.hide_viewport}
             for o in head.children if o.name.startswith("Face_")}
    report["face_children_of_head"] = faces
    report["head_children"] = sorted(o.name for o in head.children)
    log("faces", json.dumps(faces))
    os.remove(pal_path)
    return report


BAR_HEIGHT = 0.90
STOOL_HEIGHT = 0.62


def type_summary(root):
    summ = summarize(root)
    return summ


def run_customers(out_dir, preview_dir, do_render=True, do_export=True, only=None):
    """Prompt 3: every type -> previews, FBX, re-import check; then the two line-ups."""
    results = {}
    names = [n for n in TYPES if not only or n in only]
    for name in names:
        reset_scene()
        pal, pal_path = build_palette(out_dir)
        mat = make_material(pal_path)
        root = build_figure(name, dict(TYPES[name]), pal, mat, set())
        summ = summarize(root)
        res = {"summary": summ, "es": TYPES[name].get("es")}
        log("%s: %d triangles, %d mesh objects, height %.3f m" % (
            name, summ["triangles"], summ["mesh_objects"], summ["height_m"]))
        if summ["triangles"] > 2500:
            log("WARNING %s is over the 2,500 triangle budget" % name)
        if do_render:
            render_views(root, os.path.join(preview_dir, name))
        if do_export:
            path = os.path.join(out_dir, "Customer_%s.fbx" % name)
            export_fbx(root, path)
            res["reimport"] = reimport_check(path, expected_for(root, summ))
            log("reimport %s ok=%s %s" % (name, res["reimport"]["ok"], res["reimport"]["notes"]))
        results[name] = res
    lineups = None
    if do_render and not only:
        lineups = render_lineups(out_dir, preview_dir)
    return results, lineups


def render_lineups(out_dir, preview_dir):
    reset_scene()
    pal, pal_path = build_palette(out_dir)
    mat = make_material(pal_path)
    entries = [("%s\n%s" % (n, TYPES[n]["es"]), n, dict(TYPES[n])) for n in TYPES]
    roots, total = build_lineup(entries, pal, mat, set(), gap=0.3)
    render_group(os.path.join(preview_dir, "lineup_standing.png"), roots, total, (0, 1, -0.4))
    # seated: stools 0.62 m, bar block 0.90 m in front (between the player and the customers)
    for o in [o for o in bpy.data.objects if o.get("render_prop") and o.type == 'FONT']:
        bpy.data.objects.remove(o)
    wood = flat_material("Preview_Stool", "#8A5A3A")
    bar_mat = flat_material("Preview_Bar", "#B5835A")
    check = {}
    for r in roots:
        x = r.location.x
        prop_box("Stool", (0.36, 0.36, STOOL_HEIGHT), (x, 0.05, STOOL_HEIGHT / 2), wood)
        set_seated(r, STOOL_HEIGHT)
        S = r["size_scale"]
        z0 = r.location.z
        head_top = world_bbox(mesh_objects(r))[1].z
        check[r["fonda_type"]] = {
            "root_z_seated_m": round(z0, 3),
            "eye_z_m": round(z0 + 0.63 * S, 3),
            "chin_z_m": round(z0 + 0.42 * S, 3),
            "top_z_m": round(head_top, 3),
            "eyes_above_bar": z0 + 0.63 * S > BAR_HEIGHT,
            "chin_above_bar": z0 + 0.42 * S > BAR_HEIGHT,
        }
    prop_box("Bar", (total + 0.6, 0.5, BAR_HEIGHT), (0, -0.75, BAR_HEIGHT / 2), bar_mat)
    for r in roots:
        text_label(r["fonda_type"], (r.location.x, -1.01, 0.45), size=0.12, colour="#FFFFFF")
    render_group(os.path.join(preview_dir, "lineup_seated.png"), roots, total, (0, 1, -0.12),
                 height=1.6)
    log("seated check", json.dumps(check))
    return check


# ---------------------------------------------------------------------------
# Grandma pieces (Prompt 4): sleeves and bracelet for the player's XR hands
# ---------------------------------------------------------------------------
# Built directly in Blender metres. The wrist opening is at the origin and the tube runs
# toward -Y in Blender, which is +Z in Unity after the FBX axis conversion
# (axis_forward='-Z', axis_up='Y': Blender -Y -> FBX +Z -> Unity +Z).
# The flowers sit on the tube's top (+Z Blender = +Y Unity, the back of the forearm).

GRANDMA = {
    "sleeve_body": "#F2E3C6", "sleeve_cuff": "#C8553D",
    "flowers": ("#F7D046", "#F28AB2", "#FFFFFF"),
    "beads": ("#2F5DA8", "#F5C518"),
    "inner_d_wrist": 0.075, "inner_d_elbow": 0.095, "length": 0.16, "wall": 0.008,
    "cuff": 0.02, "sides": 12,
    "bead_r": 0.006, "ring_r": 0.04, "beads_n": 12,
}


def annulus(t0, t1, ri0, ri1, ro0, ro1, sides):
    """Closed tube segment along -Y from y=-t0 to y=-t1 (inner/outer radii at both ends)."""
    bm = bmesh.new()

    def ring(t, r):
        return [bm.verts.new((r * math.cos(2 * math.pi * k / sides), -t,
                              r * math.sin(2 * math.pi * k / sides))) for k in range(sides)]
    o0, o1, i0, i1 = ring(t0, ro0), ring(t1, ro1), ring(t0, ri0), ring(t1, ri1)
    for k in range(sides):
        n = (k + 1) % sides
        bm.faces.new((o0[k], o0[n], o1[n], o1[k]))
        bm.faces.new((i0[k], i1[k], i1[n], i0[n]))
        bm.faces.new((o0[k], i0[k], i0[n], o0[n]))
        bm.faces.new((o1[k], o1[n], i1[n], i1[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def build_sleeve(side, palette, material, colours_used, coll):
    g = GRANDMA
    L, w, sides = g["length"], g["wall"], g["sides"]
    ri_w, ri_e = g["inner_d_wrist"] / 2, g["inner_d_elbow"] / 2

    def ri(t):
        return ri_w + (ri_e - ri_w) * t / L

    b = PartBuilder(palette, colours_used)
    c = g["cuff"]
    # cuff band: terracotta, 2 mm proud of the body so it reads as a band
    b.raw(annulus(0.0, c, ri(0), ri(c), ri(0) + w + 0.002, ri(c) + w + 0.002, sides), g["sleeve_cuff"])
    b.raw(annulus(c, L, ri(c), ri(L), ri(c) + w, ri(L) + w, sides), g["sleeve_body"])
    # three tiny flower squares on the outside (top), mirrored between L and R
    mirror = -1 if side == "L" else 1
    for (t, ang), col in zip(((0.05, 12), (0.09, -10), (0.13, 8)), g["flowers"]):
        a = math.radians(90 + mirror * ang)
        r = ri(t) + w
        pos = (r * math.cos(a), -t, r * math.sin(a))
        b.box((0.012, 0.012, 0.004), pos, col, radius=0.002, seg=1, rot=(0, -(a - math.pi / 2), 0))
    name = "Grandma_Sleeve_" + side
    me = b.to_mesh_raw(name)
    return new_mesh_object(name, me, coll, (0, 0, 0), None, material)


def build_bracelet(palette, material, colours_used, coll):
    g = GRANDMA
    b = PartBuilder(palette, colours_used)
    for k in range(g["beads_n"]):
        a = 2 * math.pi * k / g["beads_n"]
        pos = (g["ring_r"] * math.cos(a), 0.0, g["ring_r"] * math.sin(a))
        b.sphere(g["bead_r"], pos, g["beads"][k % 2], segs=6, rings=4)
    me = b.to_mesh_raw("Grandma_Bracelet")
    return new_mesh_object("Grandma_Bracelet", me, coll, (0, 0, 0), None, material)


def piece_summary(ob):
    bpy.context.view_layer.update()
    mn, mx = world_bbox([ob])
    return {
        "root": ob.name, "objects": [ob.name], "mesh_objects": 1,
        "triangles": sum(len(p.vertices) - 2 for p in ob.data.polygons),
        "height_m": round(mx.z - mn.z, 4),
        "bbox_min": [round(v, 4) for v in mn], "bbox_max": [round(v, 4) for v in mx],
        "size_m": [round(v, 4) for v in (mx - mn)],
        "hierarchy": hierarchy(ob),
    }


def reimport_check_piece(path, expected):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    new = list(bpy.data.objects)
    bpy.context.view_layer.update()
    res = {"file": os.path.relpath(path, REPO_ROOT), "ok": True, "notes": []}
    roots = [o for o in new if o.parent is None]
    if len(roots) != 1 or roots[0].type != 'MESH':
        res["ok"] = False
        res["notes"].append("expected one mesh root, got %s" % [o.name for o in new])
        return res
    ob = roots[0]
    rot = [round(math.degrees(a), 3) for a in ob.matrix_world.to_euler()]
    res.update(root=ob.name, root_rotation_deg=rot,
               root_world_scale=[round(s, 4) for s in ob.matrix_world.to_scale()],
               origin=[round(v, 5) for v in ob.matrix_world.translation])
    if any(abs(a) > 0.01 for a in rot):
        res["ok"] = False
        res["notes"].append("root has rotation %s" % rot)
    if ob.matrix_world.translation.length > 1e-4:
        res["ok"] = False
        res["notes"].append("origin moved")
    mn, mx = world_bbox([ob])
    size = [round(v, 4) for v in (mx - mn)]
    res["size_m"] = size
    res["expected_size_m"] = expected["size_m"]
    for a, e in zip(size, expected["size_m"]):
        if abs(a - e) > max(0.001, 0.02 * e):
            res["ok"] = False
            res["notes"].append("size %s != %s" % (size, expected["size_m"]))
            break
    res["bbox_min"] = [round(v, 4) for v in mn]
    res["bbox_max"] = [round(v, 4) for v in mx]
    for a, e in zip(res["bbox_min"] + res["bbox_max"], expected["bbox_min"] + expected["bbox_max"]):
        if abs(a - e) > 0.001:
            res["ok"] = False
            res["notes"].append("bbox moved: %s %s" % (res["bbox_min"], res["bbox_max"]))
            break
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    res["triangles"] = tris
    if tris != expected["triangles"]:
        res["ok"] = False
        res["notes"].append("triangles %d != %d" % (tris, expected["triangles"]))
    if ob.name.startswith("Grandma_Sleeve"):
        # the wrist end is at y = 0 and the tube runs toward -Y (Unity +Z)
        res["tube_direction"] = "-Y" if mn.y < -0.1 and abs(mx.y) < 0.002 else "unexpected"
        if res["tube_direction"] != "-Y":
            res["ok"] = False
            res["notes"].append("sleeve does not run from y=0 toward -Y")
    res["materials"] = sorted({s.material.name for s in ob.material_slots if s.material})
    return res


def placeholder_hand(coll=None):
    skin = flat_material("Preview_Hand", "#E8B98F")
    obs = []
    # forearm stub through the sleeve, palm and a thumb block (render only)
    me = bpy.data.meshes.new("Forearm")
    bm = make_cylinder(0.03, 0.03, 0.22, 12)  # already along Y
    bm.transform(Matrix.Translation((0, -0.09, 0)))
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Forearm", me)
    me.materials.append(skin)
    bpy.context.scene.collection.objects.link(ob)
    obs.append(ob)
    obs.append(prop_box("Palm", (0.08, 0.09, 0.03), (0, 0.02 + 0.045, 0), skin))
    obs.append(prop_box("Thumb", (0.022, 0.045, 0.022), (0.05, 0.045, 0.005), skin))
    return obs


def run_grandma(out_dir, preview_dir, do_render=True, do_export=True):
    results = {}
    pieces = (("Grandma_Sleeve_L", lambda pal, mat, u, c: build_sleeve("L", pal, mat, u, c)),
              ("Grandma_Sleeve_R", lambda pal, mat, u, c: build_sleeve("R", pal, mat, u, c)),
              ("Grandma_Bracelet", build_bracelet))
    for name, fn in pieces:
        reset_scene()
        pal, pal_path = build_palette(out_dir)
        mat = make_material(pal_path)
        coll = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(coll)
        ob = fn(pal, mat, set(), coll)
        summ = piece_summary(ob)
        log("%s: %d triangles, size %s m" % (name, summ["triangles"], summ["size_m"]))
        if summ["triangles"] > 600:
            log("WARNING %s is over the 600 triangle budget" % name)
        res = {"summary": summ}
        if do_render:
            # look from the wrist end so the opening and the cuff read with flat lighting
            cam = camera("Cam_piece", (0, -0.07 if "Sleeve" in name else 0, 0), (-0.5, -0.6, -0.45), 0.26)
            render(os.path.join(preview_dir, "Grandma", name + ".png"), cam)
        if do_export:
            path = os.path.join(out_dir, name + ".fbx")
            export_fbx(ob, path)
            res["reimport"] = reimport_check_piece(path, summ)
            log("reimport %s ok=%s %s" % (name, res["reimport"]["ok"], res["reimport"]["notes"]))
        results[name] = res
    if do_render:
        render_grandma_fit(out_dir, preview_dir)
    return results


def build_grandma_set(pal, mat, offset=(0, 0, 0), scale=1.0, bracelet_offset=0.008):
    """Right forearm: sleeve + bracelet around the wrist, placed for previews."""
    obs = []
    for name, fn in (("R", lambda c: build_sleeve("R", pal, mat, set(), c)),
                     ("B", lambda c: build_bracelet(pal, mat, set(), c))):
        coll = bpy.data.collections.new("Grandma_" + name)
        bpy.context.scene.collection.children.link(coll)
        ob = fn(coll)
        obs.append(ob)
    obs[1].location.y = bracelet_offset
    return obs


def render_grandma_fit(out_dir, preview_dir):
    reset_scene()
    pal, pal_path = build_palette(out_dir)
    mat = make_material(pal_path)
    build_grandma_set(pal, mat)
    placeholder_hand()
    grid = floor_grid(-0.3, 0.3, -0.3, 0.3, 0.05, line=0.0015)  # 5 cm grid
    for g in grid:
        g.location.z = -0.06
    cam = camera("Cam_fit", (0, -0.03, 0), (-0.6, -0.55, -0.45), 0.36)
    render(os.path.join(preview_dir, "Grandma", "how_it_sits.png"), cam)
    cam2 = camera("Cam_fit_top", (0, -0.03, 0), (0.0, 0.0001, -1), 0.36)
    render(os.path.join(preview_dir, "Grandma", "how_it_sits_top.png"), cam2)


# ---------------------------------------------------------------------------
# Final run (Prompt 5): everything, contact sheet, report.json, re-run stability
# ---------------------------------------------------------------------------

STABLE_KEYS = ("objects", "mesh_objects", "triangles", "height_m", "bbox_min", "bbox_max", "hierarchy")


def raw_fbx_root_transform(path):
    """Read the root node's Lcl transform straight from the FBX file (what Unity reads)."""
    try:
        from io_scene_fbx import parse_fbx
    except ImportError:
        import addon_utils
        addon_utils.enable("io_scene_fbx")
        from io_scene_fbx import parse_fbx
    root_elem, version = parse_fbx.parse(path)
    out = {"fbx_version": version}

    def find(elem, name):
        return [e for e in elem.elems if e.id == name]
    objects = find(root_elem, b"Objects")[0]
    conns = find(root_elem, b"Connections")[0]
    parented = {c.props[1] for c in conns.elems if c.props[0] == b"OO" and c.props[2] != 0}
    models = [m for m in find(objects, b"Model")]
    roots = [m for m in models if m.props[0] not in parented or
             not any(c.props[1] == 0 and c.props[0] == m.props[0] for c in [])]
    # roots = models connected to the scene root (uid 0)
    to_scene = {c.props[1] for c in conns.elems if c.props[0] == b"OO" and c.props[2] == 0}
    roots = [m for m in models if m.props[0] in to_scene]
    out["roots"] = []
    for m in roots:
        name = m.props[1].split(b"\x00")[0].decode()
        props = {}
        for p70 in find(m, b"Properties70"):
            for pe in p70.elems:
                key = pe.props[0].decode()
                if key in ("Lcl Translation", "Lcl Rotation", "Lcl Scaling", "PreRotation"):
                    props[key] = [round(float(v), 4) for v in pe.props[4:7]]
        out["roots"].append({"name": name, **props})
    gs = find(root_elem, b"GlobalSettings")
    if gs:
        g = {}
        for p70 in find(gs[0], b"Properties70"):
            for pe in p70.elems:
                key = pe.props[0].decode()
                if key in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis",
                           "CoordAxisSign", "UnitScaleFactor"):
                    g[key] = pe.props[4]
        out["global_settings"] = g
    return out


def group_under(objs, name, location, scale, rot_z=0.0):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.location = location
    e.scale = (scale, scale, scale)
    e.rotation_euler = (0, 0, rot_z)
    for o in objs:
        if o.parent is None:
            o.parent = e
    return e


def render_contact_sheet(out_dir, preview_dir):
    reset_scene()
    pal, pal_path = build_palette(out_dir)
    mat = make_material(pal_path)
    names = list(TYPES)
    widths = [0.62 * GLOBAL_SCALE * TYPES[n]["size"] * max(1.0, TYPES[n]["wide"]) for n in names]
    xs, total = lineup_positions(widths, 0.35)
    z_stand = 2.5
    wood = flat_material("Preview_Stool", "#8A5A3A")
    for n, x in zip(names, xs):
        build_figure(n, dict(TYPES[n]), pal, mat, set(), offset=(x, 0, z_stand))
        text_label("%s\n%s" % (n, TYPES[n]["es"]), (x, -0.4, z_stand - 0.12), size=0.12)
        r = build_figure(n, dict(TYPES[n]), pal, mat, set(), offset=(x, 0, 0))
        set_seated(r, STOOL_HEIGHT)
        prop_box("Stool", (0.36, 0.36, STOOL_HEIGHT), (x, 0.05, STOOL_HEIGHT / 2), wood)
    left = -total / 2 - 0.25
    text_label("Standing", (left, -0.4, z_stand + 0.7), size=0.16, rot=(math.pi / 2, 0, math.pi / 2 * 0))
    text_label("Seated (0.62 m stool)", (left + 0.6, -0.4, 1.95), size=0.16)
    # grandma pieces, scaled x4 for legibility
    z_g = -1.05
    gx = [-2.7, -1.0, 0.7, 2.6]
    for i, (side, label) in enumerate((("L", "Grandma_Sleeve_L"), ("R", "Grandma_Sleeve_R"))):
        coll = bpy.data.collections.new(label)
        bpy.context.scene.collection.children.link(coll)
        ob = build_sleeve(side, pal, mat, set(), coll)
        group_under([ob], "G_" + label, (gx[i], 0, z_g), 4.0, rot_z=math.radians(-60))
        text_label(label, (gx[i], -0.6, z_g - 0.62), size=0.12)
    coll = bpy.data.collections.new("Grandma_Bracelet")
    bpy.context.scene.collection.children.link(coll)
    ob = build_bracelet(pal, mat, set(), coll)
    group_under([ob], "G_Bracelet", (gx[2], 0, z_g), 4.0, rot_z=math.radians(-30))
    text_label("Grandma_Bracelet", (gx[2], -0.6, z_g - 0.62), size=0.12)
    before = set(bpy.data.objects)
    build_grandma_set(pal, mat)
    placeholder_hand()
    fit = [o for o in bpy.data.objects if o not in before]
    group_under(fit, "G_Fit", (gx[3], 0, z_g), 4.0, rot_z=math.radians(-60))
    text_label("how it sits (placeholder hand)", (gx[3], -0.6, z_g - 0.62), size=0.12)
    text_label("Grandma pieces (shown x4)", (left + 0.75, -0.4, -0.25), size=0.16)
    # frame everything
    scene = bpy.context.scene
    width, z_lo, z_hi = total + 1.2, -1.85, z_stand + 1.75
    scene.render.resolution_x = 2560
    scene.render.resolution_y = int(round(2560 * (z_hi - z_lo) / width))
    cam = camera("Cam_sheet", (0, 0, (z_lo + z_hi) / 2), (-0.2, 1, -0.08), width, distance=20)
    cam.data.sensor_fit = 'HORIZONTAL'
    path = os.path.join(preview_dir, "contact_sheet.png")
    render(path, cam)
    return path


def file_sha256(path):
    import hashlib
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def run_all(out_dir, preview_dir, report_path, do_render=True):
    previous = None
    if os.path.exists(report_path):
        with open(report_path) as f:
            previous = json.load(f)
    customers, seated = run_customers(out_dir, preview_dir, do_render=do_render)
    grandma = run_grandma(out_dir, preview_dir, do_render=do_render)
    sheet = render_contact_sheet(out_dir, preview_dir) if do_render else None
    files = {}
    for name, res in list(customers.items()) + list(grandma.items()):
        fname = ("Customer_%s.fbx" % name) if name in TYPES else (name + ".fbx")
        path = os.path.join(out_dir, fname)
        summ = res["summary"]
        entry = {k: summ[k] for k in STABLE_KEYS}
        entry["root"] = summ["root"]
        if name in TYPES:
            entry["type_es"] = TYPES[name]["es"]
            entry["budget_triangles"] = 2500
        else:
            entry["size_m"] = summ["size_m"]
            entry["budget_triangles"] = 600
        entry["within_budget"] = summ["triangles"] <= entry["budget_triangles"]
        entry["reimport_check"] = res.get("reimport")
        entry["raw_fbx"] = raw_fbx_root_transform(path)
        files[os.path.relpath(path, REPO_ROOT)] = entry
    pal_path = os.path.join(out_dir, PALETTE_NAME)
    pal = Palette(all_palette_colours())
    report = {
        "generator": "Tools/Blender/fonda_figure.py --type all",
        "blender_version": bpy.app.version_string,
        "global_scale": GLOBAL_SCALE,
        "bevel_segments": {"head_torso": HEAD_SEG, "other": LIMB_SEG},
        "palette": {"file": os.path.relpath(pal_path, REPO_ROOT), "size_px": PALETTE_SIZE,
                    "colours": len(pal.colours), "sha256": file_sha256(pal_path),
                    "order": pal.colours},
        "material": MATERIAL_NAME,
        "files": files,
        "seated_bar_check": {"bar_height_m": BAR_HEIGHT, "stool_height_m": STOOL_HEIGHT,
                             "per_type": seated},
        "all_reimport_checks_ok": all(e["reimport_check"] and e["reimport_check"]["ok"] for e in files.values()),
        "all_within_budget": all(e["within_budget"] for e in files.values()),
        "previews": {"contact_sheet": sheet and os.path.relpath(sheet, REPO_ROOT)},
    }
    # re-run stability: compare names, counts and dimensions with the previous report
    if previous and "files" in previous:
        diffs = []
        for k in sorted(set(files) | set(previous["files"])):
            a, b = files.get(k), previous["files"].get(k)
            if a is None or b is None:
                diffs.append("%s only in %s run" % (k, "previous" if a is None else "this"))
                continue
            for key in STABLE_KEYS:
                if json.dumps(a[key], sort_keys=True) != json.dumps(b[key], sort_keys=True):
                    diffs.append("%s: %s differs" % (k, key))
        if previous.get("palette", {}).get("sha256") != report["palette"]["sha256"]:
            diffs.append("palette PNG bytes differ")
        report["rerun_check"] = {"compared_with": "previous report.json", "identical": not diffs,
                                 "differences": diffs,
                                 "note": "FBX bytes are not compared: the exporter writes a "
                                         "creation timestamp. Names, counts, triangles, "
                                         "hierarchy, heights and bounding boxes are compared."}
    else:
        report["rerun_check"] = {"compared_with": None, "identical": None,
                                 "note": "no previous report.json; run --type all again to compare"}
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2, sort_keys=True)
    log("report", os.path.relpath(report_path, REPO_ROOT), "rerun identical:", report["rerun_check"]["identical"])
    return report


# ---------------------------------------------------------------------------
# Export and re-import check
# ---------------------------------------------------------------------------

def export_fbx(root, path):
    """Export one character (everything in its collection) with the spec's FBX settings.

    Deviation from the spec: use_selection=False + collection=<the character's collection>.
    Blender cannot keep hidden objects selected, so use_selection=True silently dropped the
    hidden Face_Happy / Face_Angry. The collection holds exactly this character, so the
    exported set is the same, and hidden faces are written with FBX Visibility = 0.

    Deviation 2: path_mode='STRIP' instead of 'COPY'. COPY wrote a Customer_X.fbm/ copy of
    the palette per file plus this machine's absolute path; STRIP references just
    "Fonda_Palette.png", which sits next to every FBX in Assets/Art/Characters/.
    """
    coll = root.users_collection[0]
    objs = set([root] + descendants(root))
    extra = [o.name for o in coll.all_objects if o not in objs]
    if extra:
        raise RuntimeError("collection %s holds other objects: %s" % (coll.name, extra))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, collection=coll.name, use_selection=False,
        object_types={'EMPTY', 'MESH'}, apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=False, mesh_smooth_type='FACE', add_leaf_bones=False,
        path_mode='STRIP', embed_textures=False)
    log("export", os.path.relpath(path, REPO_ROOT))


def summarize(root):
    """Names, counts, dimensions of a built hierarchy (used for re-run stability)."""
    objs = [root] + descendants(root)
    mn, mx = world_bbox(mesh_objects(root))
    o = root.matrix_world.translation
    return {
        "root": root.name,
        "objects": sorted(x.name for x in objs),
        "mesh_objects": len(mesh_objects(root)),
        "triangles": triangle_count(root),
        "height_m": round(mx.z - o.z, 4),
        "bbox_min": [round(v, 4) for v in (mn - o)],
        "bbox_max": [round(v, 4) for v in (mx - o)],
        "hierarchy": hierarchy(root),
    }


def reimport_check(path, expected):
    """Import the FBX into an empty scene and check rotation, height and facing.

    expected: summary dict from summarize() of the source hierarchy, plus optional
    "facing_probe" (name of a mesh whose centre must be on the -Y side of "facing_ref").
    """
    # A truly empty scene: the caller's scene is discarded (rebuild afterwards if needed).
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    new = list(bpy.data.objects)
    bpy.context.view_layer.update()
    roots = [o for o in new if o.parent is None]
    res = {"file": os.path.relpath(path, REPO_ROOT), "ok": True, "notes": []}
    if len(roots) != 1:
        res["ok"] = False
        res["notes"].append("expected 1 root, found %d: %s" % (len(roots), [r.name for r in roots]))
    root = roots[0] if roots else None
    if root:
        rot = [round(math.degrees(a), 3) for a in root.matrix_world.to_euler()]
        scl = [round(s, 4) for s in root.matrix_world.to_scale()]
        res["root"] = root.name
        res["root_rotation_deg"] = rot
        res["root_world_scale"] = scl
        if any(abs(a) > 0.01 for a in rot):
            res["ok"] = False
            res["notes"].append("root has rotation %s" % rot)
        meshes = [o for o in new if o.type == 'MESH']
        mn, mx = world_bbox(meshes)
        o = root.matrix_world.translation
        h = mx.z - o.z
        res["height_m"] = round(h, 4)
        res["expected_height_m"] = expected["height_m"]
        err = abs(h - expected["height_m"]) / expected["height_m"]
        res["height_error_pct"] = round(err * 100, 3)
        if err > 0.02:
            res["ok"] = False
            res["notes"].append("height off by %.2f%%" % (err * 100))
        names = sorted(x.name for x in new)
        res["objects"] = names
        if names != expected["objects"]:
            res["ok"] = False
            res["notes"].append("object names differ: %s" % sorted(set(names) ^ set(expected["objects"])))
        tris = sum(sum(len(p.vertices) - 2 for p in m.data.polygons) for m in meshes)
        res["triangles"] = tris
        if tris != expected["triangles"]:
            res["ok"] = False
            res["notes"].append("triangles %d != %d" % (tris, expected["triangles"]))
        probe = expected.get("facing_probe")
        ref = expected.get("facing_ref")
        if probe and ref:
            pm = [m for m in new if m.name == probe]
            rm = [m for m in new if m.name == ref]
            if pm and rm:
                pc = sum((pm[0].matrix_world @ v.co for v in pm[0].data.vertices), Vector()) / len(pm[0].data.vertices)
                rc = rm[0].matrix_world.translation
                dy = pc.y - rc.y
                res["facing"] = "-Y" if dy < 0 else "+Y"
                res["facing_probe_dy_m"] = round(dy, 4)
                if dy >= 0:
                    res["ok"] = False
                    res["notes"].append("%s is not on the -Y side of %s" % (probe, ref))
            else:
                res["ok"] = False
                res["notes"].append("facing probe objects missing")
        hidden = expected.get("hidden")
        if hidden is not None:
            vis = {m.name: (not m.hide_viewport and not m.hide_get()) for m in new if m.name.startswith("Face_")}
            res["face_visibility"] = vis
        mats = sorted({s.material.name for m in meshes for s in m.material_slots if s.material})
        res["materials"] = mats
    return res


# ---------------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------------

def log(*a):
    print("[fonda]", *a, flush=True)


def all_palette_colours():
    """Colours of every type and every optional part, so the palette is shared and stable."""
    colours = {INK, MOUTH, CHEEKS}
    for p in [TEST_TYPE] + list(TYPES.values()):
        for k in ("skin", "hair", "shirt", "trousers", "shoes", "brows", "mustache", "hat_colour"):
            if p.get(k):
                colours.add(p[k])
        if p.get("skin"):
            colours.add(darken(p["skin"], 0.85))
    colours |= EXTRA_PART_COLOURS
    colours |= {GRANDMA["sleeve_body"], GRANDMA["sleeve_cuff"], *GRANDMA["flowers"], *GRANDMA["beads"]}
    return colours


EXTRA_PART_COLOURS = set(PART_COLOURS.values())


def build_palette(out_dir):
    pal = Palette(all_palette_colours())
    path = os.path.join(out_dir, PALETTE_NAME)
    pal.write_png(path)
    log("palette", os.path.relpath(path, REPO_ROOT), "%d colours" % len(pal.colours))
    return pal, path


def run_single(type_arg, out_dir, preview_dir, do_render=True, do_export=True):
    reset_scene()
    pal, pal_path = build_palette(out_dir)
    mat = make_material(pal_path)
    name, p = type_params(type_arg)
    used = set()
    root = build_figure(name, p, pal, mat, used)
    summ = summarize(root)
    summ.update(facing_probe="Head", facing_ref="Head")
    log("%s: %d triangles, %d mesh objects, height %.3f m" % (name, summ["triangles"], summ["mesh_objects"], summ["height_m"]))
    result = {"summary": summ}
    if do_render:
        render_views(root, os.path.join(preview_dir, name))
    if do_export:
        path = os.path.join(out_dir, "Customer_%s.fbx" % name)
        export_fbx(root, path)
        result["reimport"] = reimport_check(path, expected_for(root, summ))  # resets the scene
        log("reimport", json.dumps(result["reimport"]))
    return result


def expected_for(root, summ):
    e = dict(summ)
    faces = [o for o in descendants(root) if o.name.startswith("Face_Neutral")]
    if faces:
        e.update(facing_probe=faces[0].name, facing_ref="Head", hidden=True)
    else:
        e.update(facing_probe="Head", facing_ref="Hips")
    return e


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(prog="fonda_figure.py")
    ap.add_argument("--type", default="test",
                    help="test | parts | customers | grandma | all | <Type> (one customer)")
    ap.add_argument("--out", default=DEFAULT_OUT, help="export folder for .fbx and the palette")
    ap.add_argument("--previews", default=DEFAULT_PREVIEWS, help="folder for preview renders")
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--no-export", action="store_true")
    ap.add_argument("--report", default=os.path.join(HERE, "report.json"),
                    help="report path for --type all")
    ap.add_argument("--json", default=None, help="write the result summary to this JSON file")
    return ap.parse_args(argv)


def main():
    args = parse_args()
    out = os.path.abspath(args.out)
    prev = os.path.abspath(args.previews)
    if args.type == "parts":
        res = run_parts(prev)
    elif args.type == "all":
        res = run_all(out, prev, os.path.abspath(args.report), not args.no_render)
    elif args.type == "grandma":
        res = run_grandma(out, prev, not args.no_render, not args.no_export)
    elif args.type == "customers":
        res, lineups = run_customers(out, prev, not args.no_render, not args.no_export)
        res = {"customers": res, "seated_check": lineups}
    else:
        res = run_single(args.type, out, prev, not args.no_render, not args.no_export)
    if args.json:
        with open(args.json, "w") as f:
            json.dump(res, f, indent=2, sort_keys=True)


if __name__ == "__main__":
    main()
