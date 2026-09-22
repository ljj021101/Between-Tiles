# Between Tiles

Between Tiles is a grid-based puzzle game and level-authoring toolkit built in Unity. The project separates puzzle rules from presentation, provides an automatic solvability checker, and includes an in-editor workflow for creating and validating levels.

## Highlights

- Data-driven grid runtime with separate cell, entity, and edge rules.
- Pushable boxes, ice sliding, chained pushes, locks, lasers, goals, and world-map entrances.
- Breadth-first search solver that reports solvability and minimum move count.
- Configurable search limit of up to 2,000,000 visited states per validation run.
- Undo support through board snapshots.
- Persistent completion and cleared-lock state using JSON and `PlayerPrefs`.
- Custom Unity level editor with brushes, selection translation, laser previews, autosave, and one-click validation.

## Architecture

| Area | Main files | Responsibility |
| --- | --- | --- |
| Rules and data | `GridTypes.cs`, `LevelData.cs`, `LevelAsset.cs` | Defines cells, entities, edges, entrances, locks, and serialized level assets. |
| Runtime simulation | `BoardRuntime.cs` | Resolves movement, pushes, ice behavior, locks, laser paths, and snapshots. |
| Solver | `LevelSolver.cs` | Encodes states and performs BFS with hash-based duplicate detection. |
| Game flow | `PrototypeGameController.cs`, `GameSaveSystem.cs` | Handles input, animation, level transitions, undo, and saved progress. |
| Authoring tools | `LevelEditorController.cs`, `LevelEditorControllerEditor.cs` | Provides scene-view editing, previews, saving, and automated validation. |

## Controls

| Input | Action |
| --- | --- |
| `WASD` / Arrow keys | Move |
| `Z` | Undo the previous move |
| `R` | Reload the current level |
| `Esc` | Return to the previous level or quit |

## Run the Project

1. Install Unity `6000.3.10f1`.
2. Clone this repository and open its root folder in Unity Hub.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Enter Play Mode.

## Create and Validate a Level

1. Create a level asset from `Assets > Create > Between Tiles > Level`.
2. Use the custom scene tools to paint cells, place entities, and configure entrances, locks, and lasers.
3. Save the scene data to the selected `LevelAsset`.
4. Run the solvability check to obtain the minimum move count or identify an invalid level.

## Tech Stack

Unity, C#, Unity Input System, ScriptableObject, custom Unity Editor tooling, BFS, JSON, and `PlayerPrefs`.

