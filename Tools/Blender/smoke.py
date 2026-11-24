import bpy, os, sys
out = sys.argv[sys.argv.index("--") + 1]
bpy.ops.mesh.primitive_cube_add(); s = bpy.context.scene; s.render.engine = 'BLENDER_WORKBENCH'
s.render.resolution_x = s.render.resolution_y = 256; s.render.filepath = out
bpy.ops.render.render(write_still=True)
