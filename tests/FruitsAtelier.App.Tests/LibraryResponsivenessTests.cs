using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using Microsoft.Data.Sqlite;
using L = FruitsAtelier.Localization.Strings;

internal static class LibraryResponsivenessTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "atelier-library-lock-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            _ = new LibraryDatabase(root, "");
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = Path.Combine(root, "library.db"), Pooling = false }.ToString());
            db.Open();
            var view = new EditorView();
            var canvas = new RecordingCanvas();
            WithWriteLock(() => view.InitializeLibrary(true, new LibrarySettings { Workspace = root }),
                "library initialization blocked the UI on the database writer");
            Settle();
            WithWriteLock(() =>
            {
                var text = canvas.Texts.Single(t => t.Value == L.Get("library.projects"));
                view.PointerDown(text.X + 2, text.Y + 2, 0, false, false);
                view.PointerUp(text.X + 2, text.Y + 2, 0);
                view.Render(canvas, 980, 620);
                view.CloseLibrary();
            }, "My projects blocked the UI on the database writer");
            Settle();

            void WithWriteLock(Action action, string message)
            {
                using var transaction = db.BeginTransaction();
                using var command = db.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE maps SET stamp=stamp";
                command.ExecuteNonQuery();
                var operation = Task.Run(action);
                bool responsive;
                try { responsive = operation.Wait(TimeSpan.FromSeconds(2)); }
                finally { transaction.Rollback(); }
                operation.GetAwaiter().GetResult();
                if (!responsive) throw new Exception(message);
            }
            void Settle()
            {
                for (int i = 0; i < 250; i++)
                {
                    canvas.Clear(); view.Render(canvas, 980, 620);
                    if (i > 20 && !view.LibraryLoading) return;
                    Thread.Sleep(20);
                }
                throw new Exception("Library background work did not finish after releasing the writer");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
