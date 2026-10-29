using System.Buffers.Binary;
using System.Text;
namespace MiniDb;
// Each record is contained in one page. [used:u16][length:u16,payload]...; page 0 is a file header.
public static class PageFormat
{
    public const int PageSize=4096;
    public static byte[] Encode(IEnumerable<byte[]> records)
    {
        using var stream=new MemoryStream(); var page=new byte[PageSize]; "MINIDB01"u8.CopyTo(page); stream.Write(page); page=new byte[PageSize]; int offset=2;
        foreach(var record in records)
        {
            if(record.Length>PageSize-4) throw new SqlException("Строка или схема превышает лимит страницы: 4092 байта");
            if(offset+2+record.Length>PageSize) { BinaryPrimitives.WriteUInt16LittleEndian(page,(ushort)offset); stream.Write(page); page=new byte[PageSize]; offset=2; }
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(offset),(ushort)record.Length); record.CopyTo(page,offset+2); offset+=record.Length+2;
        }
        BinaryPrimitives.WriteUInt16LittleEndian(page,(ushort)offset); stream.Write(page); return stream.ToArray();
    }
    public static IEnumerable<byte[]> Decode(byte[] bytes)
    {
        if(bytes.Length<PageSize || bytes.Length%PageSize!=0 || !bytes.AsSpan(0,8).SequenceEqual("MINIDB01"u8)) throw new SqlException("Повреждён заголовок базы");
        for(int page=PageSize;page<bytes.Length;page+=PageSize)
        {
            int used=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(page)); if(used<2 || used>PageSize) throw new SqlException("Повреждена страница");
            for(int off=2;off<used;) { if(off+2>used) throw new SqlException("Повреждена запись"); int n=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(page+off)); if(n==0 || off+2+n>used) throw new SqlException("Повреждена длина записи"); yield return bytes.AsSpan(page+off+2,n).ToArray(); off+=n+2; }
        }
    }
}
public class RecordWriter
{
    readonly MemoryStream stream=new();
    public void Int(long n) { Span<byte> b=stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b,n); stream.Write(b); }
    public void Text(string s) { var b=Encoding.UTF8.GetBytes(s); Int(b.Length); stream.Write(b); }
    public void Value(object v) { Int(v is long?0:1); if(v is long n) Int(n); else Text((string)v); }
    public byte[] Bytes()=>stream.ToArray();
}
public class RecordReader(byte[] bytes)
{
    int position;
    public long Int() { if(position+8>bytes.Length) throw new SqlException("Оборванная запись"); long n=BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(position)); position+=8; return n; }
    public string Text() { long length=Int(); if(length<0 || length>bytes.Length-position) throw new SqlException("Повреждена строка"); string s=Encoding.UTF8.GetString(bytes,position,(int)length); position+=(int)length; return s; }
    public object Value() { long tag=Int(); return tag switch { 0=>Int(),1=>Text(),_=>throw new SqlException("Неизвестный тип") }; }
}
public static class DatabaseImage
{
    public static byte[] Save(Dictionary<string,Table> tables) => PageFormat.Encode(Records(tables));
    static IEnumerable<byte[]> Records(Dictionary<string,Table> tables)
    {
        foreach(var t in tables.Values)
        {
            var w=new RecordWriter(); w.Int(1); w.Text(t.Name); w.Int(t.NextId); w.Int(t.Columns.Count); foreach(var c in t.Columns) { w.Text(c.Name); w.Text(c.Type); w.Int(c.PrimaryKey?1:0); } yield return w.Bytes();
            foreach(var row in t.Rows) { w=new(); w.Int(2); w.Text(t.Name); w.Int(row.Key); foreach(var value in row.Value) w.Value(value); yield return w.Bytes(); }
        }
    }
    public static Dictionary<string,Table> Load(byte[] bytes)
    {
        var tables=new Dictionary<string,Table>(StringComparer.OrdinalIgnoreCase);
        foreach(var record in PageFormat.Decode(bytes))
        {
            var r=new RecordReader(record); long kind=r.Int(); string name=r.Text();
            if(kind==1) { long next=r.Int(), count=r.Int(); if(count<1 || count>256) throw new SqlException("Некорректная схема"); var columns=new List<ColumnDefinition>(); for(int i=0;i<count;i++) columns.Add(new(r.Text(),r.Text(),r.Int()!=0)); tables.Add(name,new(name,columns){NextId=next}); }
            else if(kind==2) { var t=tables[name]; long id=r.Int(); var row=new object[t.Columns.Count]; for(int i=0;i<row.Length;i++) row[i]=r.Value(); t.Rows.Add(id,row); }
            else throw new SqlException("Неизвестный вид записи");
        }
        return tables;
    }
}
