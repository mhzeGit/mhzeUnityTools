# Play From Scene View

## What This Tool Does
Press **Alt+P** and Unity enters Play Mode with the player standing exactly where the Scene view camera is, looking exactly the way the Scene view looks. The Game view opens on the same picture the Scene view showed, so there is no jump when you press the key. It is Unity's version of Unreal's "Play From Camera".

## Why It Helps
- Test the part of the level you are working on without walking there or moving the player by hand.
- Nothing is saved: the scene is not touched. Only the running player moves, so the player's start position in the scene stays where you authored it.
- Press **Alt+P** again while playing to stop.

## How To Use
1. Fly the Scene view to where you want to start. Look the way you want to face.
2. Press **Alt+P** (or **Tools > Gigaduck > Play From Scene View**).
3. Play starts with the player's eyes at the Scene view camera.

Change the shortcut in **Edit > Shortcuts...** (category `Gigaduck`). Options are in **Edit > Preferences > Gigaduck > Play From Scene View**.

## How It Works
1. When you press the shortcut the Scene view camera pose is saved in `SessionState` (it survives the domain reload).
2. Unity enters Play Mode with your usual Enter Play Mode settings.
3. As soon as the scene has loaded, before the first frame, a *placer* finds the player and moves it so that the **player's camera** matches the saved pose (position and rotation). The player keeps its own yaw and pitch, so mouse look carries on smoothly.
4. If no player exists yet (it is spawned a little later), it keeps looking for a few seconds (default 5 s).

## Which Players Work
| Placer | Finds | Priority |
|---|---|---|
| MHZE First Person Controller (`Integrations/MhzeFirstPerson`, compiled only when `com.mhze.firstperson-controller` is installed) | `FirstPersonController` | 100 |
| Generic | The object tagged `Player` (tag is configurable), using its child `Camera`. Handles `CharacterController` and `Rigidbody`. | 0 |

If the player has no child camera, the player object itself is treated as the eye. With **Move main camera if no player** turned on, scenes with only a camera move that camera instead.

### Writing a placer for another rig
Rigs that keep their own yaw and pitch values need to be told about the new angles. Implement `IPlayFromViewPlacer` in an editor assembly and register it once:

```csharp
[InitializeOnLoad]
static class MyRigPlacerRegistration
{
    static MyRigPlacerRegistration() => PlayFromView.RegisterPlacer(new MyRigPlacer());
}

sealed class MyRigPlacer : IPlayFromViewPlacer
{
    public string Name => "My rig";
    public int Priority => 100;   // above the generic placer (0)

    public bool TryPlace(in ViewPose pose, out PlacementReport report)
    {
        // Find the rig. Return false if it is not there (yet).
        // Use pose.Position, pose.Yaw (degrees around Y) and pose.Pitch (positive looks down).
        report = new PlacementReport(rig.name, positionError, angleError);
        return true;
    }
}
```

## When It Falls Back To Normal Play
A warning is logged and Play Mode starts normally when the Scene view is in 2D mode or orthographic (isometric), when there is no Scene view, or when a prefab stage is open (the Scene view is then not looking at the game scene).

## Notes
- The player is placed by its **camera**, not its feet. If you press the shortcut in mid-air the player falls from there, and a camera inside a wall puts the player inside the wall.
- The Scene view field of view is not copied: the player camera keeps its own, so the framing can differ slightly even though position and direction match.
- The pitch is limited to the controller's own vertical range.
- Scripts that move the player during their own start-up can still override the placement; they run in the same frame, so look at the console line to see what was placed.
- Needs Unity 6000.0 or newer.
