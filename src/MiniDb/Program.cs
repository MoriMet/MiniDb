using System.Text;
using MiniDb;
Console.InputEncoding=Encoding.UTF8;
Console.OutputEncoding=new UTF8Encoding(false);
string? Option(string name) { int i=Array.IndexOf(args,name); return i>=0 && i+1<args.Length ? args[i+1] : null; }
try
{
    string path=Option("--db")??"minidb.db";
    if(args.Contains("--crash-worker"))
    {
        using var worker=new Database(path);
        worker.Execute("BEGIN"); worker.Execute("UPDATE users SET age=99 WHERE id=1"); worker.Execute("INSERT INTO users VALUES(9,'Crash',99)");
        string point=Option("--point")??"uncommitted"; string signal=Option("--signal")??throw new ArgumentException("--signal required");
        void Pause() { File.WriteAllText(signal,"ready"); Thread.Sleep(Timeout.Infinite); }
        if(point=="uncommitted") Pause();
        WalStore.TestHook=p=> { if(p==point) Pause(); };
        worker.Execute("COMMIT"); return;
    }
    using var db=new Database(path);
    if(db.Recovered) Console.WriteLine("WAL: подтверждённые изменения восстановлены.");
    if(Option("--file") is string file) { bool failed=false; foreach(string sql in SqlScript.Split(File.ReadAllText(file))) { try { Print(db.Execute(sql)); } catch(Exception e) when(e is SqlException or IOException or OverflowException) { Console.Error.WriteLine("Ошибка: "+e.Message); failed=true; } } Environment.ExitCode=failed?1:0; return; }
    Console.WriteLine("MiniDb. .help — справка, .exit — выход. SQL завершайте точкой с запятой.");
    var input=new StringBuilder();
    while(true)
    {
        Console.Write("db> "); string? line=Console.ReadLine(); if(line==null) break;
        if(input.Length==0 && line.Trim() is ".exit" or ".quit" or "exit") break;
        if(input.Length==0 && line.Trim()==".help") { Console.WriteLine("CREATE TABLE/INDEX, INSERT INTO, SELECT, UPDATE, DELETE, BEGIN, COMMIT, ROLLBACK, EXPLAIN SELECT. См. README.md."); continue; }
        input.AppendLine(line);
        var parts=SqlScript.SplitComplete(input.ToString(),out string tail);
        foreach(string sql in parts) try { Print(db.Execute(sql)); } catch(Exception e) when(e is SqlException or IOException or OverflowException or InvalidCastException) { Console.WriteLine("Ошибка: "+e.Message); }
        input.Clear(); input.Append(tail);
    }
}
catch(Exception e) { Console.Error.WriteLine("Ошибка: "+e.Message); Environment.ExitCode=1; }
static void Print(QueryResult result)
{
    if(result.Columns.Length>0)
    {
        var widths=result.Columns.Select((c,i)=>Math.Min(80,Math.Max(c.Length,result.Rows.Count==0?0:result.Rows.Max(r=>r[i].ToString()?.Length??0)))).ToArray();
        string Format(object[] row)=>string.Join(" | ",row.Select((v,i)=>(v.ToString()??"").PadRight(widths[i])));
        Console.WriteLine(Format(result.Columns.Cast<object>().ToArray())); Console.WriteLine(string.Join("-+-",widths.Select(w=>new string('-',w))));
        foreach(var row in result.Rows) Console.WriteLine(Format(row));
    }
    if(result.Message.Length>0) Console.WriteLine(result.Message);
}
public static class SqlScript
{
    public static List<string> SplitComplete(string text,out string tail)
    {
        var result=new List<string>(); int start=0; bool quoted=false;
        for(int i=0;i<text.Length;i++) { if(text[i]=='\'') { if(quoted && i+1<text.Length && text[i+1]=='\'') { i++; continue; } quoted=!quoted; } else if(text[i]==';'&&!quoted) { if(!string.IsNullOrWhiteSpace(text[start..i])) result.Add(text[start..(i+1)]); start=i+1; } }
        tail=text[start..]; if(string.IsNullOrWhiteSpace(tail)) tail=""; return result;
    }
    public static IEnumerable<string> Split(string text) { var result=SplitComplete(text,out var tail); if(tail.Length>0) result.Add(tail); return result; }
}
