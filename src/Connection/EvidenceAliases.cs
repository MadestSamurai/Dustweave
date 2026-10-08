using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace BD2Daily.Live
{
    [DataContract] public sealed class ClientAlias
    {
        [DataMember] public string Original="", Current="";
    }
    // Keep workflow observation keys stable while reflection uses this client's names.
    internal sealed class EvidenceAliases
    {
        private readonly Dictionary<string,string> forward=new Dictionary<string,string>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> reverse=new Dictionary<string,string>(StringComparer.Ordinal);
        private static readonly Regex tokens=new Regex(@"[\u0370-\u1fff]+",RegexOptions.Compiled);
        internal EvidenceAliases(IEnumerable<ClientAlias> aliases)
        {
            foreach(var alias in aliases)
            {
                string existing;
                if(forward.TryGetValue(alias.Original,out existing)&&existing!=alias.Current||reverse.TryGetValue(alias.Current,out existing)&&existing!=alias.Original)
                    throw new InvalidOperationException("Conflicting evidence names");
                forward[alias.Original]=alias.Current;reverse[alias.Current]=alias.Original;
            }
        }
        internal string Current(string path){return tokens.Replace(path,m=>forward.ContainsKey(m.Value)?forward[m.Value]:m.Value);}
        internal string Original(string path){return tokens.Replace(path,m=>reverse.ContainsKey(m.Value)?reverse[m.Value]:m.Value);}
    }
}
