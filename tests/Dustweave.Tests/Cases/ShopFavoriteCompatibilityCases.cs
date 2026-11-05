using BD2Daily.Live;

static class ShopFavoriteCompatibilityCases
{
    sealed class Store {
        internal static Dictionary<int,HashSet<int>>? Value=new(){[7]=[41,42],[9]=[5]};
        private static Dictionary<int,HashSet<int>> α()=>Value!;
        public static void SaveFavorite(int shop,int product,bool add,Action callback)=>throw new Exception("Read must not submit a save");
    }
    internal static void Run(List<string> cases){
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add(label);}
        string Key(long[][] rows)=>string.Join(";",rows.Select(r=>string.Join(",",r)).Order());
        Check(Key(NativeShopFavorites.Read(typeof(Store)))=="7,41;7,42;9,5","shop favorites resolve a renamed native reader and retain exact identities without submitting a save");
        Store.Value=null;bool rejected=false;try{NativeShopFavorites.Read(typeof(Store));}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"missing current favorites stay unknown instead of fabricating an empty user selection");
        Store.Value=new(){[9]=[5]};Check(Key(NativeShopFavorites.Read(typeof(Store)))=="9,5","favorites read resumes after native data arrives without restarting the tool");
        Store.Value=[];Check(NativeShopFavorites.Read(typeof(Store)).Length==0,"confirmed empty favorites remain a valid empty selection");
    }
}
