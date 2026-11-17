using System;
using System.Collections.Generic;
using Mobeen.CommandHistory;
using History = Mobeen.CommandHistory.CommandHistory;

internal static class Program
{
    private static int failures;
    private static int tests;

    private static void Check(bool condition, string message = "Assertion failed")
    { if (!condition) throw new Exception(message); }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static void Test(string name, Action action)
    {
        tests++;
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private static DelegateCommand Add(Action<int> change, int amount) =>
        new DelegateCommand(() => change(amount), () => change(-amount));

    private static int Main()
    {
        Test("execute, undo, and redo preserve state", () =>
        {
            int value = 0; var history = new History();
            history.Execute(Add(n => value += n, 3));
            Check(value == 3 && history.UndoCount == 1);
            Check(history.Undo() && value == 0 && history.RedoCount == 1);
            Check(history.Redo() && value == 3 && history.RedoCount == 0);
        });
        Test("new branch invalidates redo", () =>
        {
            int value = 0; var history = new History();
            history.Execute(Add(n => value += n, 3)); history.Undo();
            history.Execute(Add(n => value += n, 7));
            Check(value == 7 && !history.CanRedo);
        });
        Test("empty history operations return false", () =>
        { var h = new History(); Check(!h.Undo() && !h.Redo() && !h.CanUndo); });
        Test("capacity evicts the oldest command", () =>
        {
            int value = 0; var h = new History(2);
            for (int i = 0; i < 3; i++) h.Execute(Add(n => value += n, 1));
            Check(h.UndoCount == 2); h.Undo(); h.Undo();
            Check(value == 1 && !h.Undo());
        });
        Test("failed execute preserves existing redo", () =>
        {
            int value = 0; var h = new History();
            h.Execute(Add(n => value += n, 1)); h.Undo();
            Throws<InvalidOperationException>(() => h.Execute(new DelegateCommand(
                () => throw new InvalidOperationException("execute failed"), () => { })));
            Check(h.UndoCount == 0 && h.RedoCount == 1 && value == 0);
            h.Redo(); Check(value == 1);
        });
        Test("failed undo leaves history in place", () =>
        {
            var h = new History(); h.Execute(new DelegateCommand(() => { },
                () => throw new InvalidOperationException("undo failed")));
            Throws<InvalidOperationException>(() => h.Undo());
            Check(h.UndoCount == 1 && h.RedoCount == 0);
        });
        Test("failed redo can be retried", () =>
        {
            bool fail = false; int value = 0; var h = new History();
            h.Execute(new DelegateCommand(() =>
            { if (fail) throw new InvalidOperationException(); value++; }, () => value--));
            h.Undo(); fail = true; Throws<InvalidOperationException>(() => h.Redo());
            Check(h.RedoCount == 1 && value == 0);
            fail = false; h.Redo(); Check(value == 1);
        });
        Test("clear forgets entries without mutating application state", () =>
        {
            int value = 0; var h = new History(); h.Execute(Add(n => value += n, 5));
            h.Clear(); Check(value == 5 && !h.CanUndo && !h.CanRedo);
        });
        Test("nested history mutation is rejected and guard recovers", () =>
        {
            var h = new History(); var command = new DelegateCommand(() => { }, () => { });
            Throws<InvalidOperationException>(() => h.Execute(new DelegateCommand(
                () => h.Execute(command), () => { })));
            Check(h.UndoCount == 0); h.Execute(command); Check(h.UndoCount == 1);
        });
        Test("invalid capacities and null callbacks are rejected", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new History(0));
            Throws<ArgumentOutOfRangeException>(() => new History(-1));
            Throws<ArgumentNullException>(() => new History().Execute(null));
            Throws<ArgumentNullException>(() => new DelegateCommand(null, () => { }));
            Throws<ArgumentNullException>(() => new DelegateCommand(() => { }, null));
        });
        Test("composite undo uses reverse order", () =>
        {
            string value = ""; var h = new History();
            h.Execute(new CompositeCommand(new[]
            {
                new DelegateCommand(() => value += "A", () => { Check(value == "A"); value = ""; }),
                new DelegateCommand(() => value += "B", () => { Check(value == "AB"); value = "A"; })
            }));
            Check(value == "AB"); h.Undo(); Check(value == ""); h.Redo(); Check(value == "AB");
        });
        Test("failed composite execute rolls back completed commands", () =>
        {
            int value = 0; var h = new History();
            var group = new CompositeCommand(new[] { Add(n => value += n, 2),
                new DelegateCommand(() => throw new InvalidOperationException(), () => { }) });
            Throws<InvalidOperationException>(() => h.Execute(group));
            Check(value == 0 && h.UndoCount == 0);
        });
        Test("failed composite undo restores already undone commands", () =>
        {
            int value = 0; var h = new History();
            h.Execute(new CompositeCommand(new[]
            {
                new DelegateCommand(() => value += 1, () => throw new InvalidOperationException()),
                Add(n => value += n, 10), Add(n => value += n, 100)
            }));
            Throws<InvalidOperationException>(() => h.Undo());
            Check(value == 111 && h.UndoCount == 1 && h.RedoCount == 0);
        });
        Test("rollback failures preserve both errors", () =>
        {
            var group = new CompositeCommand(new[]
            {
                new DelegateCommand(() => { }, () => throw new ArgumentException("rollback")),
                new DelegateCommand(() => throw new InvalidOperationException("execute"), () => { })
            });
            var error = Throws<AggregateException>(() => group.Execute());
            Check(error.InnerExceptions.Count == 2);
            Check(error.InnerExceptions[0] is InvalidOperationException);
        });
        Test("composite copies its input collection", () =>
        {
            int value = 0; var commands = new List<IUndoableCommand> { Add(n => value += n, 1) };
            var group = new CompositeCommand(commands); commands.Add(Add(n => value += n, 10));
            group.Execute(); Check(value == 1); group.Undo(); Check(value == 0);
        });
        Test("composite validates members", () =>
        {
            Throws<ArgumentNullException>(() => new CompositeCommand(null));
            Throws<ArgumentException>(() => new CompositeCommand(new IUndoableCommand[] { null }));
        });
        Console.WriteLine($"{tests - failures}/{tests} tests passed");
        return failures == 0 ? 0 : 1;
    }
}
