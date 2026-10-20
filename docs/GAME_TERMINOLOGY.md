# Game terminology

UI terminology was checked against the locally installed game's `NameTextTable` and `LocalTextTable` on 2026-10-20. The reviewed term list is in [`game-terms.json`](../src/Desktop/Localization/game-terms.json); each entry records its table and row. Captured tables and game assemblies are not distributed with this document.

Use the named game's locale, not a mechanical conversion of the Simplified name:

| Concept | 简体中文 | 繁體中文 | English |
| --- | --- | --- | --- |
| Talent consumable | 天赋神药 | 天賦特效藥 | Ability Pill |
| Talent | 制伏 | 壓制 | Overpower |
| Talent | 探查 | 搜索 | Search |
| Talent | 隐身 | 藏身 | Stealth |
| Talent | 飞奔 | 飛奔 | Rush |
| Pack | 游戏卡 | 遊戲卡帶 | Pack |
| Equipment material | 精炼粉末 | 強化粉末 | Refining Powder |
| Mirror Wars consumable | 鲜血鸡尾酒 | 血之雞尾酒 | Blood Cocktail |
| Difficulty | 极难 | 超困難 | Very Hard |
| Tower minigame | 使徒运气防守 | 使者運氣防守 | Apostle Random Defense |
| Action minigame | 恶魔猎人：怒火边缘 | 惡魔獵人：怒火邊緣 | Fiend Hunter: Edge of Fury |
| Character relationship | 亲密度 | 親密度 | Rapport |
| Crop | 藏红花 | 番紅花 | Saffron |
| Crop | 土豆 | 馬鈴薯 | Potato |
| Territory dish | 魅惑炒饭 | 雙峰炒飯 | Curvaceous Fried Rice |
| Territory dish | 爱欲浓汤 | 魅影濃湯 | Libido Soup |

Relevant contextual rows include `LocalTextTable/125700` (action minigame), `2710001` (Rapport), `NameTextTable/7811` (powder), `81022` (Granhildr / 葛蘭西特), `1710025` and `1710030` (territory dishes).

Apply the glossary only at the presentation boundary. Host templates are corrected **before** formatting arguments; account names, paths, task IDs, settings, original messages and receipts keep their original values. Integrated tool catalogs share the glossary through the host's process-local presentation adapter. Traditional rendering retains its reverse lookup so changing language does not change filter selections.

Do not replace ambiguous words without context. NPC affinity is distinct from costume Rapport; Fiend Hunter is distinct from Fiend Hunter: Edge of Fury; equipment refinement is distinct from alchemy. General software actions such as “开始”, “暂停”, “重试” and task grouping names may remain descriptive instead of copying a game button. Dynamic recipe and map names should continue to use their locale-specific table fields.

When adding or changing a term, record its source, review all three locales, and run the tool-language check. That check covers historical source matching, placeholder preservation, locale switching and the actual integrated catalogs. Do not change gameplay identifiers to match display text.
