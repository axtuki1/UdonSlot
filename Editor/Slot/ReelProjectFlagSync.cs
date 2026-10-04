using System;
using System.Collections.Generic;

namespace AX.UdonSlot.REV1_0.Editor
{
    public static class ReelProjectFlagSync
    {
        public sealed class Entry
        {
            public string name, display;
            public float bet1, bet2, bet3;
            public bool remainder;
            public List<string> roles = new List<string>();
            public Entry Copy() { return new Entry {name=name,display=display,bet1=bet1,bet2=bet2,bet3=bet3,remainder=remainder,roles=new List<string>(roles)}; }
        }
        // Build the entire replacement before touching SerializedObject or the editing asset.
        public static List<Entry> Plan(List<Entry> existing, ReelControlProject.Flag flag, string[] availableRoles)
        {
            if (string.IsNullOrWhiteSpace(flag.name)) throw new InvalidOperationException("フラグ名が未指定です。");
            var names = new HashSet<string>(); var result = new List<Entry>();
            foreach (var entry in existing)
            {
                if (!names.Add(entry.name)) throw new InvalidOperationException("抽選表のフラグ名が重複しています: " + entry.name);
                result.Add(entry.Copy());
            }
            var roles = new HashSet<string>();
            foreach (var role in flag.roles)
                if (availableRoles == null || Array.IndexOf(availableRoles,role) < 0 || !roles.Add(role))
                    throw new InvalidOperationException("成立役が不正です: " + role);
            string source = string.IsNullOrEmpty(flag.lotterySourceName) ? flag.name : flag.lotterySourceName;
            int index = result.FindIndex(x => x.name == source);
            if (index < 0 && !string.IsNullOrEmpty(flag.lotterySourceName)) throw new InvalidOperationException("元フラグが抽選表にありません: " + source);
            if (result.Exists(x => x.name == flag.name && x.name != source)) throw new InvalidOperationException("変更先のフラグ名が登録済みです: " + flag.name);
            if (index < 0) { index = result.Count; result.Add(new Entry {name=flag.name,display=flag.name}); }
            result[index].name = flag.name; result[index].roles = new List<string>(flag.roles);
            return result;
        }
    }
}
