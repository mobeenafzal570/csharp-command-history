# C# command history

[![Build and test](https://github.com/mobeenafzal570/csharp-command-history/actions/workflows/ci.yml/badge.svg)](https://github.com/mobeenafzal570/csharp-command-history/actions/workflows/ci.yml)

A small, dependency-free undo/redo library for synchronous C# applications. It provides bounded history, grouped commands, redo-branch invalidation, and explicit failure behavior.

The library targets **.NET Standard 2.1**. The executable regression checks run on **.NET 10**, with GitHub Actions configured for Linux and Windows. It has no Unity dependency; Unity integration can use the same command interfaces.

## Example

```csharp
using Mobeen.CommandHistory;

int score = 0;
var history = new CommandHistory(capacity: 50);

history.Execute(new DelegateCommand(
    execute: () => score += 10,
    undo: () => score -= 10));

history.Undo(); // score = 0
history.Redo(); // score = 10
```

For game actions, implement `IUndoableCommand` and capture the state needed to reverse one validated action. Keep visuals and asynchronous animation outside this synchronous history layer.

## Grouped actions

```csharp
history.Execute(new CompositeCommand(new IUndoableCommand[]
{
    new DelegateCommand(() => score += 3, () => score -= 3),
    new DelegateCommand(() => score += 7, () => score -= 7)
}));

history.Undo(); // reverses the second command, then the first
```

If grouped execution fails, previously completed commands are undone in reverse order. If grouped undo fails, already undone commands are re-executed in their original order. Failures during recovery are surfaced in an `AggregateException`, preserving the original error as its first entry.

## Invariants

| Operation | Behavior |
| --- | --- |
| Execute a new action after undo | Discards the old redo branch only after execution succeeds. |
| Exceed capacity | Evicts the oldest undo entry. Application state remains applied. |
| Callback throws | The history entry stays in its previous stack. |
| Empty Undo or Redo | Returns `false`. |
| Clear | Discards both stacks without changing application state. |
| Re-enter history from a callback | Throws rather than allowing nested mutation to corrupt ordering. |

## Run the checks

Install the .NET 10 SDK, then run:

```sh
dotnet build src/CommandHistory/CommandHistory.csproj --configuration Release
dotnet run --project tests/CommandHistory.Tests/CommandHistory.Tests.csproj --configuration Release
```

The test executable returns a nonzero exit code on failure. It uses only the standard library: no test framework or package restore dependency is needed beyond the SDK reference packs.

**16 behavioral checks** cover undo/redo, branching, bounded retention, failed callbacks, reentrancy, grouped ordering, rollback/restoration, input validation, and recovery failures.

## Design boundaries

- Single-threaded and synchronous. The caller serializes access.
- Individual commands must validate before mutating, or provide their own exception-safe behavior. The history cannot repair arbitrary partial changes inside a callback that throws.
- Group recovery is best effort. A recovery exception means application state may require manual repair.
- Capacity limits entry count, not the memory captured by each command. Commands and captured resources are not automatically disposed when an entry is forgotten.
- The portable library is built and tested with .NET. A particular Unity scene, input flow, or device integration needs its own validation.

## Repository layout

- `src/CommandHistory`: portable library and public interfaces.
- `tests/CommandHistory.Tests`: runnable regression checks.
- `.github/workflows/ci.yml`: Linux and Windows builds and checks.

This is a newly implemented portfolio utility, developed with AI assistance. Its scope, tests, and failure guarantees are documented so the code can be evaluated directly.
