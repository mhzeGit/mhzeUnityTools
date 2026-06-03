# Missing Script Inspector

Unity Editor tool that finds GameObjects with missing (deleted) MonoBehaviour scripts and recovers what the original script was and what serialized data was stored on it.

## How it works

When a MonoBehaviour script is deleted, Unity still preserves the serialized data in the `.unity` / `.prefab` YAML files, including the original script's GUID and all custom field values. This tool reads that data directly from the source files.

## Usage

- **Tools > Missing Script Inspector** — open the inspector window
- **GameObject > Missing Script Inspector** — scan the current selection from the hierarchy context menu

In the window:
1. Select GameObjects and click **Scan Selected**, or click **Scan Active Scene**
2. Click any result to see the original script name, asset path, and all serialized fields

## Technical details

- Uses reflection to read `m_PathID` from `SerializedProperty` PPtrs
- Parses YAML from `.unity` / `.prefab` files to locate the `MonoBehaviour` block and extract GUID + field data
- Resolves script name via `AssetDatabase.GUIDToAssetPath`
- Handles both scene objects and prefab instances (reads from the `.prefab` file for prefab instances)
