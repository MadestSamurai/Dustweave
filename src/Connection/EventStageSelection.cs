using System;
using System.Linq;
using System.Reflection;

namespace BD2Daily.Live
{
    internal static class EventStageSelection
    {
        internal static void Select<TRow>(object lobby, TRow row, Action<TRow, bool> select) where TRow : class
        {
            if (row == null) throw new ArgumentNullException("row");
            // Native SetSelectItem updates the ID first, then returns early for the
            // same row object. Scrolling can recycle that object for another stage,
            // leaving its detail panel and sweep/auto buttons on the previous stage.
            // Resolve only the native selected-row reference, independent of its name.
            var fields = lobby.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(TRow)).ToArray();
            if (fields.Length != 1)
                throw new InvalidOperationException("Event selected-row reference could not be resolved; no battle or sweep submitted");
            if (ReferenceEquals(fields[0].GetValue(lobby), row)) fields[0].SetValue(lobby, null);
            // Keep native deselection, rewards, detail and button refresh together.
            // This changes presentation only, never stage progress or eligibility.
            select(row, true);
        }
    }
}
