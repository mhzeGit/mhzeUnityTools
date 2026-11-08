"""Texture groups of the boiler room (the per-environment data file read by gm_groups.py).

TEXTURE_FILE  texture name -> finalized 2048 px albedo in .utmp/<slug>/tex
REGION        region prefix of the part names (<Region>__<Part>) -> (texture, tile in metres per texture repeat, textureRotation or None)
GROUPS        group -> which assets share one Graphmat project / 2048 atlas and how their regions are textured
              assets = [(Asset, module[, grm])]  grm = Euler degrees applied to the asset while texturing (undone on import)
              regions = region prefixes present in the group      ao_radius = AO ray length in metres (about 1/8 of the group's typical size)
              cube = side of the bounding cube (cylinder groups / brick-course alignment)   cylinder = "Y" for one round asset (axis rotated to Y)
              over = per-region overrides {"Region": {"tile": .., "mode": "cylinder", "textureRotation": [..], "texture": ..}}
A group never mixes assets that need very different texel densities (walls, boilers and floor have their own atlas; small props share one).
"""
TEXTURE_FILE = {
    "teal_iron": "teal_iron_2k.png", "red_paint": "red_paint_2k.png", "brick": "brick_2k.png", "mortar_stone": "mortar_stone_2k.png",
    "flagstone": "flagstone_2k.png", "dark_oak": "dark_oak_2k.png", "pine_planks": "pine_planks_2k.png", "plaster": "plaster_2k.png",
    "black_iron": "black_iron_2k.png", "brass_cream": "brass_cream_2k.png", "amber_glass": "amber_glass_2k.png",
    "brick_wall": "brick_wall_2k.png",      # assembled tile (tools/make_brick_tile.py): 0.96 m x 0.96 m, 0.24 x 0.12 m bricks in running bond
}

# region -> (texture, tile metres, texture rotation or None)
REGION = {
    "Teal": ("teal_iron", 1.2, None),
    "Iron": ("black_iron", 0.6, None),
    "Red": ("red_paint", 0.5, None),
    "Brick": ("brick_wall", 0.96, None),
    "Mortar": ("mortar_stone", 0.8, None),
    "Flag": ("flagstone", 1.4, None),
    "Oak": ("dark_oak", 0.7, None),
    "Pine": ("pine_planks", 0.8, None),
    "Plaster": ("plaster", 1.6, None),
    "Brass": ("brass_cream", 0.5, None),
    "Glow": ("amber_glass", 0.4, None),
    "Glass": ("teal_iron", 0.6, None),
    "Stoneware": ("brass_cream", 0.5, None),
}

AXIS_Y = [-90, 0, 0]     # round asset whose axis is Z: rotate it so the axis is Y, the axis Graphmat's cylinder projection wraps around
# NOTE on frames: the OBJ is written without an axis conversion, so Graphmat's Y axis is the model's Y (depth) and its Z axis is the model's Z (up).
# Box projection therefore puts upright courses on the +/-X and +/-Y faces (plane contains Z) and lays them flat on top faces; no texture rotation is needed.

LONG_X = [0, 90, 0]      # asset whose long axis is X: rotate it so the long axis is Z while texturing (grain runs along V = Z)

GROUPS = {
    "boiler_large": dict(assets=[("Boiler_Large", "boilers")], regions=["Teal", "Iron", "Glow"], ao_radius=0.6),
    "boiler_ms": dict(assets=[("Boiler_Medium", "boilers"), ("Boiler_Small", "boilers")], regions=["Teal", "Iron"], ao_radius=0.6),
    # one group per plinth: the cylinder projection wraps round the group's single axis; the cube side is 1 m + a multiple of 0.25 m so the
    # texture's 0.125 m courses line up with the modelled courses (the base of the plinth sits on a mortar line)
    "plinth_large": dict(assets=[("Plinth_Large", "masonry", AXIS_Y)], regions=["Brick", "Mortar"], ao_radius=0.5, cylinder="Y", cube=3.25,
                         over={"Brick": {"tile": 1.0, "mode": "cylinder"}}),
    "plinth_medium": dict(assets=[("Plinth_Medium", "masonry", AXIS_Y)], regions=["Brick", "Mortar"], ao_radius=0.5, cylinder="Y", cube=2.5,
                          over={"Brick": {"tile": 1.0, "mode": "cylinder"}}),
    "plinth_small": dict(assets=[("Plinth_Small", "masonry", AXIS_Y)], regions=["Brick", "Mortar"], ao_radius=0.5, cylinder="Y", cube=2.0,
                         over={"Brick": {"tile": 1.0, "mode": "cylinder"}}),
    "stairs_brick": dict(assets=[("Stair_Brick_9", "stairs"), ("Landing_Block", "stairs"), ("Brick_Footing", "masonry")], regions=["Brick", "Mortar"], ao_radius=0.5),
    "stairs_wood": dict(assets=[("Landing_Deck", "stairs"), ("Landing_Rail", "stairs"), ("Stair_Handrail", "stairs"), ("Shelf_Table", "props")],
                        regions=["Pine", "Oak", "Iron"], ao_radius=0.3,
                        over={"Pine": {"textureRotation": [0, 0, 90]}, "Oak": {"textureRotation": [0, 0, 90]}}),   # deck boards / shelf top run along X
    "fittings": dict(assets=[("Gauge", "fittings"), ("ValveWheel", "fittings"), ("ValveBody", "fittings"), ("Pipe_Bracket", "pipes"), ("Pipe_CeilingCollar", "pipes")],
                     regions=["Brass", "Iron", "Red", "Teal"], ao_radius=0.2),
    "pipes": dict(assets=[("Pipe_Tube_1m", "pipes"), ("Pipe_Elbow90", "pipes"), ("Pipe_Band", "pipes"), ("Pipe_Cap", "pipes"), ("Pipe_BaseFlange", "pipes")],
                  regions=["Teal"], ao_radius=0.3),
    "timber": dict(assets=[("Post_Timber", "timber"), ("Beam_Timber", "timber", LONG_X), ("Brace_Timber", "timber", LONG_X)],
                   regions=["Oak", "Iron", "Mortar"], ao_radius=0.3),
    "doors": dict(assets=[("Door_Entrance", "doors"), ("Doorframe_Entrance", "doors"), ("Door_Landing", "doors"), ("Pillar_Entrance", "timber")],
                  regions=["Oak", "Iron", "Mortar"], ao_radius=0.3),
    "floor": dict(assets=[("Floor_Panel_A", "structure"), ("Floor_Panel_B", "structure"), ("Floor_Strip_W", "structure"), ("Floor_Strip_E", "structure"),
                          ("Trench_Channel", "structure"), ("Drain_Grate", "structure")], regions=["Flag", "Mortar", "Iron"], ao_radius=0.3,
                  over={"Mortar": {"texture": "black_iron", "tile": 0.6}}),   # dark joints between the slabs like the concept
    "wall_plaster": dict(assets=[("Wall_Plaster_Panel", "structure")], regions=["Plaster", "Brick"], ao_radius=0.4),
    # cube 5.04 = 0.96 + 17 x 0.24: the panel's bottom-left corner sits on a brick-pattern boundary (tile 0.96 m)
    "wall_brick": dict(assets=[("Wall_Brick_Panel", "structure")], regions=["Brick"], ao_radius=0.4, cube=5.04),
    "ceiling": dict(assets=[("Ceiling_Bay", "structure")], regions=["Oak"], ao_radius=0.4,
                    over={"Oak": {"textureRotation": [90, 0, 90]}}),   # deck planks run along X (underside), beams along Y (their sides)
    "crates": dict(assets=[("Crate_Large", "props"), ("Crate_Xbrace", "props"), ("Crate_Small", "props"), ("Crate_Lattice", "props")],
                   regions=["Pine", "Iron"], ao_radius=0.3, over={"Pine": {"textureRotation": [90, 90, 90]}}),   # boards run horizontally
    "smallprops": dict(assets=[("Bucket", "props_misc"), ("BroomHandle", "props_misc"), ("Bottle_Jug", "props"), ("Bottle_Tall", "props"),
                               ("Bottle_Stone", "props"), ("Lantern_Wall", "props")], regions=["Iron", "Pine", "Glass", "Stoneware", "Glow"], ao_radius=0.15),
}
