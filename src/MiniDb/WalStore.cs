using System.Buffers.Binary;
using System.Security.Cryptography;
namespace MiniDb;
public sealed class WalStore : IDisposable
{
    readonly string path;
    readonly FileStream lease;
    public static Action<string>? TestHook { get; set; }
    public bool Recovered { get; private set; }
    public WalStore(string path)
    {
        this.path=Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
        lease=new FileStream(this.path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try { Recover(); } catch { lease.Dispose(); throw; }
    }
    public byte[]? Read()=>File.Exists(path)?File.ReadAllBytes(path):null;
    static void DurableWrite(FileStream file,byte[] bytes) { file.Write(bytes); file.Flush(true); }
    public void Commit(byte[] image)
    {
        using var wal=new FileStream(path+".wal",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
        wal.Position=wal.Length;
        var header=new byte[48]; "MDBWAL01"u8.CopyTo(header); BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8),image.Length); SHA256.HashData(image).CopyTo(header,16);
        wal.Write(header); DurableWrite(wal,image); TestHook?.Invoke("before-commit");
        DurableWrite(wal,"COMMIT01"u8.ToArray()); TestHook?.Invoke("after-commit");
        Checkpoint(image,true); wal.SetLength(0); wal.Flush(true);
    }
    void Checkpoint(byte[] image,bool hook)
    {
        using var data=new FileStream(path,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None);
        data.Write(image.AsSpan(0,PageFormat.PageSize)); if(hook) { data.Flush(true); TestHook?.Invoke("checkpoint"); }
        data.Write(image.AsSpan(PageFormat.PageSize)); data.SetLength(image.Length); data.Flush(true);
    }
    void Recover()
    {
        if(!File.Exists(path+".wal")) return;
        using var wal=new FileStream(path+".wal",FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        while(wal.Length-wal.Position>=48)
        {
            var header=new byte[48]; wal.ReadExactly(header);
            if(!header.AsSpan(0,8).SequenceEqual("MDBWAL01"u8)) break;
            long size=BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8));
            if(size<PageFormat.PageSize*2 || size%PageFormat.PageSize!=0 || size>int.MaxValue || size>wal.Length-wal.Position-8) break;
            var image=new byte[(int)size]; wal.ReadExactly(image); var marker=new byte[8]; wal.ReadExactly(marker);
            if(!marker.AsSpan().SequenceEqual("COMMIT01"u8) || !SHA256.HashData(image).AsSpan().SequenceEqual(header.AsSpan(16,32))) break;
            _=DatabaseImage.Load(image); Checkpoint(image,false); Recovered=true;
        }
        wal.SetLength(0); wal.Flush(true);
    }
    public void Dispose()=>lease.Dispose();
}
