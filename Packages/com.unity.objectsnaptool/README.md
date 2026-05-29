# Object Snap Tool

A simple Unity Editor tool for snapping selected objects to surfaces using keyboard shortcuts.

## Features

- **End Key**: Snap selected objects to ground (downward direction)
- **Ctrl + Arrow Keys**: Snap along X/Z axes (directional snapping)
- **Shift + Arrow Keys**: Snap along Y axis (up/down)  
- **Alt + Arrow Keys**: Snap diagonally
- **Automatic bounds adjustment**: Prevents objects from embedding in surfaces
- **Undo support**: Full integration with Unity's Undo system
- **Multi-object support**: Snap multiple selected objects at once

## Usage

1. Select one or more objects in the Scene view
2. Use keyboard shortcuts to snap in desired directions:
   - **End** - Quick snap to ground
   - **Ctrl + ↑/↓/←/→** - Snap along specific axes
   - **Shift + ↑/↓** - Vertical snapping
   - **Alt + Arrow Keys** - Diagonal snapping

Alternatively, use the menu: `Tools > Snap to Ground`

## Installation

This package is automatically recognized by Unity when placed in the `Packages` folder.

## Technical Details

- Max snap distance: 100 units
- Works with all layers by default
- Uses raycasting for precise collision detection
- Automatically adjusts for object bounds to prevent embedding
- Marks scenes as dirty when objects are moved

## Requirements

- Unity 2020.3 or later
- Objects must have colliders or renderers for proper bounds detection