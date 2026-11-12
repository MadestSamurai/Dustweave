using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Dustweave.Golden {
 [DataContract] public sealed class FormationUnit {
  [DataMember] public int Costume { get; set; }
  [DataMember] public int Position { get; set; }
 }
 // List order IS action order. Codes contain no account, instance, cost, or claimed win rate.
 [DataContract] public sealed class Formation {
  [DataMember] public int Season { get; set; } [DataMember] public int Width { get; set; } [DataMember] public int Height { get; set; }
  [DataMember] public FormationUnit[] Units { get; set; } = new FormationUnit[0];
  [DataMember] public int[] Attack { get; set; } = new int[0]; [DataMember] public int[] Defense { get; set; } = new int[0];
 }
 public static class FormationCode {
  public const string Prefix = "BD2G1.";
  public const int MaxLength = 4096;
  static void Require(bool value) { if (!value) throw new InvalidDataException("goldencode.invalid"); }
  public static void Validate(Formation f) {
   Require(f != null && f.Season >= 0 && f.Width > 0 && f.Height > 0 && f.Width <= 256 && f.Height <= 256);
   Require(f.Units != null && f.Units.Length > 0 && f.Units.Length <= 64 && f.Units.All(u => u != null && u.Costume > 0 && u.Position >= 0 && u.Position < f.Width * f.Height));
   Require(f.Units.Select(u => u.Costume).Distinct().Count() == f.Units.Length && f.Units.Select(u => u.Position).Distinct().Count() == f.Units.Length);
   foreach (var ids in new[] { f.Attack, f.Defense }) Require(ids != null && ids.Length <= 64 && ids.All(i => i > 0) && ids.Distinct().Count() == ids.Length);
  }
  static int BitsFor(int count) { int n = count - 1, bits = 0; while (n > 0) { bits++; n >>= 1; } return bits; }
  static int PositionBits(Formation f) { return BitsFor(f.Width * f.Height); }
  // CRC detects accidental corruption; uniqueness comes from retaining the FULL lossless payload,
  // not from CRC or any truncated hash. This is not authentication of the author's claims.
  static uint Crc(byte[] bytes, int length) { uint c = 0xffffffff; for (int i = 0; i < length; i++) { c ^= bytes[i]; for (int b = 0; b < 8; b++) c = (c >> 1) ^ ((c & 1) != 0 ? 0xedb88320u : 0); } return ~c; }
  public static string Encode(Formation f) {
   Validate(f);
   var direct = Payload(f, false); var delta = Payload(f, true); var w = delta.Bytes.Count < direct.Bytes.Count ? delta : direct;
   byte[] body = w.Bytes.ToArray(); uint crc = Crc(body, body.Length);
   w.Bytes.AddRange(new[] { (byte)crc, (byte)(crc >> 8), (byte)(crc >> 16), (byte)(crc >> 24) });
   return Prefix + Convert.ToBase64String(w.Bytes.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
  }
  static Bits Payload(Formation f, bool compact) {
   var w = new Bits(); w.Var((uint)f.Season); w.Var((uint)f.Width); w.Var((uint)f.Height); w.Var((uint)f.Units.Length);
   w.Write(compact ? 1u : 0u, 1); var remaining = f.Units.Select(u => u.Costume).OrderBy(n => n).ToList();
   if (compact) { int previous = 0; foreach (int id in remaining) { w.Var((uint)(id - previous)); previous = id; } }
   foreach (var u in f.Units) {
    if (compact) { int index = remaining.IndexOf(u.Costume); w.Write((uint)index, BitsFor(remaining.Count)); remaining.RemoveAt(index); } else w.Var((uint)u.Costume);
    w.Write((uint)u.Position, PositionBits(f));
   }
   foreach (var list in new[] { f.Attack, f.Defense }) { w.Var((uint)list.Length); foreach (int id in list) w.Var((uint)id); }
   return w;
  }
  public static Formation Decode(string text) {
   if (text == null || text.Length > MaxLength) throw new InvalidDataException("goldencode.invalid");
   text = text.Trim();
   if (!text.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException(text.StartsWith("BD2G", StringComparison.Ordinal) ? "goldencode.version" : "goldencode.invalid");
   string encoded = text.Substring(Prefix.Length); Require(encoded.Length > 0 && encoded.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_'));
   byte[] bytes; try { string s = encoded.Replace('-', '+').Replace('_', '/'); bytes = Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '=')); } catch (FormatException) { throw new InvalidDataException("goldencode.invalid"); }
   Require(bytes.Length >= 10);
   int end = bytes.Length - 4; uint crc = (uint)bytes[end] | (uint)bytes[end + 1] << 8 | (uint)bytes[end + 2] << 16 | (uint)bytes[end + 3] << 24;
   Require(crc == Crc(bytes, end)); var r = new Bits(bytes.Take(end).ToArray());
   var f = new Formation { Season = r.Var(), Width = r.Var(), Height = r.Var() }; Require(f.Width > 0 && f.Width <= 256 && f.Height > 0 && f.Height <= 256);
   int count = r.Var(); Require(count > 0 && count <= 64); f.Units = new FormationUnit[count];
   bool compact = r.Read(1) != 0; var remaining = new List<int>();
   if (compact) { long previous = 0; for (int i = 0; i < count; i++) { int delta = r.Var(); Require(delta > 0); previous += delta; Require(previous <= int.MaxValue); remaining.Add((int)previous); } }
   for (int i = 0; i < count; i++) {
    int costume; if (compact) { int index = (int)r.Read(BitsFor(remaining.Count)); Require(index < remaining.Count); costume = remaining[index]; remaining.RemoveAt(index); } else costume = r.Var();
    f.Units[i] = new FormationUnit { Costume = costume, Position = (int)r.Read(PositionBits(f)) };
   }
   var lists = new List<int[]>();
   for (int l = 0; l < 2; l++) { count = r.Var(); Require(count <= 64); var ids = new int[count]; for (int i = 0; i < count; i++) { ids[i] = r.Var(); Require(ids[i] > 0); } lists.Add(ids); }
   f.Attack = lists[0]; f.Defense = lists[1]; Validate(f);
   // Reject overlong varints, nonzero padding, extra bytes and noncanonical Base64 encodings.
   Require(Encode(f) == text); return f;
  }
  sealed class Bits {
   public readonly List<byte> Bytes; int position;
   public Bits() { Bytes = new List<byte>(); }
   public Bits(byte[] bytes) { Bytes = bytes.ToList(); }
   public void Write(uint value, int count) { for (int i = 0; i < count; i++, position++) { if (position / 8 == Bytes.Count) Bytes.Add(0); Bytes[position / 8] |= (byte)(((value >> i) & 1) << (position % 8)); } }
   public uint Read(int count) { Require(position + count <= Bytes.Count * 8); uint value = 0; for (int i = 0; i < count; i++, position++) value |= (uint)((Bytes[position / 8] >> (position % 8)) & 1) << i; return value; }
   public void Var(uint value) { do { uint b = value & 127; value >>= 7; Write(b | (value > 0 ? 128u : 0), 8); } while (value > 0); }
   public int Var() { uint result = 0; for (int i = 0; i < 5; i++) { uint b = Read(8); if (i == 4) Require(b < 8); result |= (b & 127) << (i * 7); if ((b & 128) == 0) return (int)result; } throw new InvalidDataException("goldencode.invalid"); }
  }
 }
 [DataContract] public sealed class FormationRules {
  [DataMember] public int Season { get; set; } [DataMember] public int Width { get; set; } [DataMember] public int Height { get; set; } [DataMember] public int MaxUnits { get; set; } [DataMember] public int AttackPoints { get; set; } [DataMember] public int DefensePoints { get; set; }
  [DataMember] public long Starts { get; set; } [DataMember] public long Ends { get; set; }
  [DataMember] public int CostLimit { get; set; } = -1; [DataMember] public int DefaultCost { get; set; } = -1;
  [DataMember] public bool CompleteCosts { get; set; }
  [DataMember] public int[] BannedPositions { get; set; } = new int[0]; [DataMember] public int[] BannedCostumes { get; set; } = new int[0]; [DataMember] public int[] BannedBlessings { get; set; } = new int[0];
  [DataMember] public Dictionary<int, int> Costs { get; set; } = new Dictionary<int, int>(); [DataMember] public Dictionary<int, int> BlessingCosts { get; set; } = new Dictionary<int, int>();
 }
 public static class FormationImport {
  // Only current game data may populate these rules. A code deliberately cannot carry prices or permissions.
  public static long Validate(Formation f, FormationRules r, IEnumerable<int> owned, long now) {
   FormationCode.Validate(f);
   if (r == null || r.Season <= 0 || now < r.Starts || now >= r.Ends) throw new InvalidDataException("goldencode.season_unavailable");
   if (f.Width != r.Width || f.Height != r.Height) throw new InvalidDataException("goldencode.board_changed");
   if (f.Units.Length > r.MaxUnits) throw new InvalidDataException("goldencode.too_many");
   var inventory = new HashSet<int>(owned);
   if (f.Units.Any(u => !inventory.Contains(u.Costume))) throw new InvalidDataException("goldencode.missing_costumes");
   if (f.Units.Any(u => r.BannedPositions.Contains(u.Position) || r.BannedCostumes.Contains(u.Costume))) throw new InvalidDataException("goldencode.banned_unit");
   long total = 0;
   if (r.CostLimit >= 0) {
    if (!r.CompleteCosts || r.DefaultCost < 0) throw new InvalidDataException("goldencode.cost_missing");
    foreach (var u in f.Units) { int cost; if (!r.Costs.TryGetValue(u.Costume, out cost)) cost = r.DefaultCost; if (cost < 0) throw new InvalidDataException("goldencode.cost_missing"); total += cost; }
    if (total > r.CostLimit) throw new InvalidDataException("goldencode.cost_exceeded");
   }
   foreach (var pair in new[] { Tuple.Create(f.Attack, r.AttackPoints), Tuple.Create(f.Defense, r.DefensePoints) }) {
    long points = 0; foreach (var id in pair.Item1) { int cost; if (r.BannedBlessings.Contains(id) || !r.BlessingCosts.TryGetValue(id, out cost) || cost < 0) throw new InvalidDataException("goldencode.bless_unavailable"); points += cost; }
    if (points > pair.Item2) throw new InvalidDataException("goldencode.bless_points");
   }
   return total;
  }
 }
}
