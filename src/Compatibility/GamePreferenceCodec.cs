using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Dustweave.Compatibility;

// Read the installed client's format description. No game code is executed and
// no client binaries or database keys are shipped with the application.
public sealed class GamePreferenceCodec
{
    private static readonly byte[] Header=Encoding.ASCII.GetBytes("SQLite format 3\0");
    private readonly byte[] key;
    public string FileName { get; }
    private GamePreferenceCodec(string password,int version)
    {
        key=Rfc2898DeriveBytes.Pbkdf2(password,Header,2010,HashAlgorithmName.SHA1,32);
        FileName=Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("ClientLocal_v"+version)));
    }
    public static GamePreferenceCodec Read(string managed)
    {
        using var module=ModuleDefinition.ReadModule(Path.Combine(managed,"Assembly-CSharp.dll"));
        var matches=new List<(string Seed,int Version)>();
        foreach(var type in module.Types) {
            if(!type.Fields.Any(f=>f.HasConstant&&Equals(f.Constant,"common-dbdata.bin")))continue;
            var versionField=type.Fields.SingleOrDefault(f=>f.IsStatic&&f.FieldType.MetadataType==MetadataType.Int32);
            var initializer=type.Methods.SingleOrDefault(m=>m.Name==".cctor");
            var assignment=initializer?.Body.Instructions.SingleOrDefault(i=>i.OpCode.Code==Code.Stsfld&&i.Operand is FieldReference f&&f.FullName==versionField?.FullName);
            int version=assignment?.Previous?.OpCode.Code switch {Code.Ldc_I4_1=>1,Code.Ldc_I4=>Convert.ToInt32(assignment.Previous.Operand),_=>0};
            if(version!=1)continue;
            foreach(var property in type.Properties.Where(p=>p.GetMethod is{IsStatic:true,HasBody:true}&&p.PropertyType.FullName=="System.String")) {
                var instructions=property.GetMethod.Body.Instructions;
                var literals=instructions.Where(i=>i.OpCode.Code==Code.Ldstr).Select(i=>(string)i.Operand).ToArray();
                var called=instructions.Select(i=>i.Operand).OfType<MethodReference>().Where(m=>m.DeclaringType.FullName==type.FullName).ToArray();
                if(literals.Length!=1||called.Length!=1)continue;
                var hash=type.Methods.SingleOrDefault(m=>m.FullName==called[0].FullName);
                if(hash?.HasBody!=true||!hash.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="System.Security.Cryptography.SHA1CryptoServiceProvider"))continue;
                matches.Add((literals[0],version));
            }
        }
        if(matches.Count!=1)throw new InvalidDataException("sandbox.preferences.format");
        return new(Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(matches[0].Seed))),matches[0].Version);
    }
    public byte[] Decode(byte[] input)
    {
        if(input.Length<512||input.Length>128*1024*1024)throw new InvalidDataException("sandbox.preferences.format");
        using var aes=Aes.Create();aes.Key=key;
        var first=aes.DecryptCbc(input.AsSpan(0,512),Header,PaddingMode.None);
        if(!first.AsSpan(0,16).SequenceEqual(Header))throw new InvalidDataException("sandbox.preferences.format");
        int page=PageSize(first);
        if(input.Length%page!=0)throw new InvalidDataException("sandbox.preferences.format");
        var output=new byte[input.Length];
        for(int offset=0;offset<input.Length;offset+=page)
            aes.DecryptCbc(input.AsSpan(offset,page),Header,output.AsSpan(offset,page),PaddingMode.None);
        return output;
    }
    public byte[] Encode(byte[] input)
    {
        int page=PageSize(input);
        if(input.Length%page!=0)throw new InvalidDataException("sandbox.preferences.format");
        using var aes=Aes.Create();aes.Key=key;var output=new byte[input.Length];
        for(int offset=0;offset<input.Length;offset+=page)
            aes.EncryptCbc(input.AsSpan(offset,page),Header,output.AsSpan(offset,page),PaddingMode.None);
        if(!Decode(output).AsSpan().SequenceEqual(input))throw new InvalidDataException("sandbox.preferences.verify");
        return output;
    }
    private static int PageSize(byte[] input)
    {
        if(input.Length<100||!input.AsSpan(0,16).SequenceEqual(Header))throw new InvalidDataException("sandbox.preferences.format");
        int page=(input[16]<<8)|input[17];if(page==1)page=65536;
        if(page<512||page>65536||(page&(page-1))!=0||input[18]!=1||input[19]!=1||input[20]!=0)
            throw new InvalidDataException("sandbox.preferences.format");
        return page;
    }
}
