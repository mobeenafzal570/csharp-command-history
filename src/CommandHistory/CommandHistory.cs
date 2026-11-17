using System;
using System.Collections.Generic;

namespace Mobeen.CommandHistory
{
    /// <summary>Callbacks must be synchronous and leave state unchanged if they throw.</summary>
    public interface IUndoableCommand
    {
        void Execute();
        void Undo();
    }

    public sealed class DelegateCommand : IUndoableCommand
    {
        private readonly Action execute;
        private readonly Action undo;

        public DelegateCommand(Action execute, Action undo)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
        }

        public void Execute() => execute();
        public void Undo() => undo();
    }

    /// <summary>A bounded, single-threaded history. Failed callbacks do not move history entries.</summary>
    public sealed class CommandHistory
    {
        private readonly LinkedList<IUndoableCommand> undo = new LinkedList<IUndoableCommand>();
        private readonly Stack<IUndoableCommand> redo = new Stack<IUndoableCommand>();
        private bool busy;

        public CommandHistory(int capacity = 100)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Capacity { get; }
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public void Execute(IUndoableCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Guard();
            busy = true;
            try
            {
                command.Execute();
                undo.AddLast(command);
                if (undo.Count > Capacity) undo.RemoveFirst();
                redo.Clear();
            }
            finally { busy = false; }
        }

        public bool Undo()
        {
            Guard();
            if (undo.Count == 0) return false;
            busy = true;
            try
            {
                IUndoableCommand command = undo.Last.Value;
                command.Undo();
                undo.RemoveLast();
                redo.Push(command);
                return true;
            }
            finally { busy = false; }
        }

        public bool Redo()
        {
            Guard();
            if (redo.Count == 0) return false;
            busy = true;
            try
            {
                IUndoableCommand command = redo.Peek();
                command.Execute();
                redo.Pop();
                undo.AddLast(command);
                return true;
            }
            finally { busy = false; }
        }

        /// <summary>Discard history without changing the application state.</summary>
        public void Clear()
        {
            Guard();
            undo.Clear();
            redo.Clear();
        }

        private void Guard()
        {
            if (busy) throw new InvalidOperationException("History cannot be changed from a command callback.");
        }
    }

    /// <summary>Group commands, reversing completed work if a later callback fails.</summary>
    public sealed class CompositeCommand : IUndoableCommand
    {
        private readonly List<IUndoableCommand> commands;

        public CompositeCommand(IEnumerable<IUndoableCommand> commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            this.commands = new List<IUndoableCommand>(commands);
            if (this.commands.Exists(command => command == null))
                throw new ArgumentException("A command group cannot contain null.", nameof(commands));
        }

        public void Execute()
        {
            int completed = 0;
            try
            {
                for (; completed < commands.Count; completed++) commands[completed].Execute();
            }
            catch (Exception original)
            {
                var failures = new List<Exception> { original };
                for (int i = completed - 1; i >= 0; i--)
                {
                    try { commands[i].Undo(); }
                    catch (Exception rollback) { failures.Add(rollback); }
                }
                if (failures.Count > 1)
                    throw new AggregateException("Execution failed and rollback was incomplete.", failures);
                throw;
            }
        }

        public void Undo()
        {
            int index = commands.Count - 1;
            try
            {
                for (; index >= 0; index--) commands[index].Undo();
            }
            catch (Exception original)
            {
                var failures = new List<Exception> { original };
                // Restore commands already undone, in their original execution order.
                for (int i = index + 1; i < commands.Count; i++)
                {
                    try { commands[i].Execute(); }
                    catch (Exception restore) { failures.Add(restore); }
                }
                if (failures.Count > 1)
                    throw new AggregateException("Undo failed and restoration was incomplete.", failures);
                throw;
            }
        }
    }
}
