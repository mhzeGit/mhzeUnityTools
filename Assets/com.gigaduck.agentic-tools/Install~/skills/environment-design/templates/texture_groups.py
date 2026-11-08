"""Texture groups of this environment (the per-environment data file read by gm_groups.py). Replace the example with the real groups.

TEXTURE_FILE  texture name -> finalized 2048 px albedo in .utmp/<slug>/tex   (written by finalize_textures.py / make_brick_tile.py)
REGION        region prefix of the part names (<Region>__<Part>) -> (texture, tile in metres per texture repeat, textureRotation or None)
GROUPS        group -> which assets share one Graphmat project / 2048 atlas and how their regions are textured
              assets = [(Asset, module[, grm])]  grm = Euler degrees applied to the asset while texturing (undone on import):
                  AXIS_Y rotates a round asset (axis Z) so its axis becomes Y (Graphmat's cylinder projection wraps round Y)
                  LONG_X rotates a long asset (length along X) so its length is Z (wood grain runs along the texture's V axis = Z)
              regions = region prefixes present in the group      ao_radius = AO ray length in metres (about 1/8 of the group's typical size)
              cube = side of the bounding cube (brick-course alignment / cylinder groups)    cylinder = "Y" for ONE round asset
              over = per-region overrides {"Region": {"tile": .., "mode": "cylinder", "textureRotation": [..], "texture": ..}}
Group compatible regions by required density, material settings and reuse; avoid an atlas per family by default.
Inspect exported UV coverage, padding, overlaps, checker density and material ownership before finalizing groups.
Prefer shared tiles/trims for repeated patterns when supported; source-image reuse alone does not avoid duplicated baked maps.
The groups below are a functional example, not an optimized grouping prescription. See references/05-graphmat-texturing.md
and references/uv-and-texture-efficiency.md in the skill folder.
"""
TEXTURE_FILE = {
    "pine_planks": "pine_planks_2k.png",
    "black_iron": "black_iron_2k.png",
    "teal_iron": "teal_iron_2k.png",
    "brick_wall": "brick_wall_2k.png",        # assembled tile (make_brick_tile.py): 0.96 m x 0.96 m, 0.24 x 0.12 m bricks in running bond
}

REGION = {
    "Pine": ("pine_planks", 0.8, None),
    "Iron": ("black_iron", 0.6, None),
    "Teal": ("teal_iron", 1.2, None),
    "Brick": ("brick_wall", 0.96, None),
}

AXIS_Y = [-90, 0, 0]
LONG_X = [0, 90, 0]

GROUPS = {
    "props": dict(assets=[("Crate_Test", "example_kit"), ("Pipe_Test", "example_kit")], regions=["Pine", "Iron", "Teal"], ao_radius=0.3),
    # cube 5.04 = 0.96 + 17 x 0.24: the panel's bottom-left corner sits on a brick-pattern boundary (tile 0.96 m)
    "wall_brick": dict(assets=[("Wall_Brick_Panel", "example_kit")], regions=["Brick"], ao_radius=0.4, cube=5.04),
}
