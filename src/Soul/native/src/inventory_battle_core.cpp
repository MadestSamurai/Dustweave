#include "bd2_inventory_battle.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

namespace {

constexpr float kTickSeconds = 0.01F;
constexpr uint64_t kMask64 = std::numeric_limits<uint64_t>::max();

constexpr int kEnergyGuard = 10;
constexpr int kCritResist = 101;
constexpr int kDebuffResist = 102;
constexpr int kStunResist = 103;
constexpr int kStaminaRegen = 104;
constexpr int kDamageReduction = 105;
constexpr int kHealAmplify = 106;
constexpr int kFireDamage = 107;
constexpr int kBuffRemoveResist = 108;
constexpr int kHealReduction = 201;
constexpr int kIncomingDamageIncrease = 202;
constexpr int kBuffBlockChance = 203;

constexpr int kRegen = 1001;
constexpr int kOverheat = 1002;
constexpr int kDrain = 1003;
constexpr int kFocus = 1004;
constexpr int kStrength = 1006;
constexpr int kCounter = 1007;
constexpr int kPoison = 2001;
constexpr int kCold = 2002;
constexpr int kDarkness = 2003;
constexpr int kBleed = 2004;

constexpr std::array<int, 7> kBuffTypes = {
    1001, 1002, 1003, 1004, 1005, 1006, 1007,
};
constexpr std::array<int, 4> kDebuffTypes = {2001, 2002, 2003, 2004};

int64_t unity_round(double value) {
    return static_cast<int64_t>(std::nearbyint(value));
}

class Rng {
public:
    explicit Rng(int32_t seed) {
        const uint32_t unsigned_seed = static_cast<uint32_t>(seed);
        for (size_t i = 0; i < state_.size(); ++i) {
            state_[i] = static_cast<uint64_t>(unsigned_seed) + i * 90ULL;
        }
    }

    uint64_t next_ulong() {
        ++draw_count_;
        const int i = index_;
        uint64_t a = state_[static_cast<size_t>(i)];
        uint64_t b = state_[static_cast<size_t>((i + 13) & 0xF)];
        const uint64_t c = a ^ b ^ (a << 16U) ^ (b << 15U);
        b = state_[static_cast<size_t>((i + 9) & 0xF)];
        b ^= b >> 11U;
        state_[static_cast<size_t>(i)] = c ^ b;
        const uint64_t d = state_[static_cast<size_t>(i)] ^
            ((state_[static_cast<size_t>(i)] << 5U) & 0xDA442D24ULL);
        index_ = (i + 15) & 0xF;
        a = state_[static_cast<size_t>(index_)];
        state_[static_cast<size_t>(index_)] =
            a ^ c ^ d ^ (a << 2U) ^ (c << 18U) ^ (b << 28U);
        return state_[static_cast<size_t>(index_)] & kMask64;
    }

    float next_float() {
        const float numerator = static_cast<float>(
            static_cast<uint32_t>(next_ulong() & 0xFFFFFFFFULL));
        return static_cast<float>(numerator / 4294967296.0F);
    }

    int range_int(int minimum, int maximum) {
        const int span = maximum - minimum;
        if (span <= 0) {
            return minimum;
        }
        return minimum + static_cast<int>(next_ulong() % static_cast<uint64_t>(span));
    }

    float range_float(float minimum, float maximum) {
        return static_cast<float>(minimum +
            static_cast<float>(next_float() * static_cast<float>(maximum - minimum)));
    }

    uint64_t draw_count() const { return draw_count_; }

private:
    std::array<uint64_t, 16> state_{};
    int index_ = 0;
    uint64_t draw_count_ = 0;
};

struct Context {
    float default_stamina = 0.0F;
    float default_stamina_recovery = 0.0F;
    float fatigue_start_time = 0.0F;
    float fatigue_interval_time = 0.0F;
    int fatigue_start_damage = 0;
    float fatigue_growth_factor = 1.0F;
    std::unordered_map<int, Bd2IbConfigInput> configs;
    std::unordered_map<int, Bd2IbTriggerInput> triggers;
    std::unordered_map<int, std::vector<Bd2IbEffectInput>> effects;
};

enum class ExpiryKind {
    StackStatus,
    ReplaceStatus,
    DirectDamage,
    FinalDamage,
    CritChance,
    BonusGain,
    UnitDamageBonus,
    UnitCooldownModifier,
    UnitCritChance,
    UnitHitRate,
};

struct Expiry {
    double at = 0.0;
    ExpiryKind kind = ExpiryKind::StackStatus;
    int key = 0;
    double amount = 0.0;
    int token = 0;
};

struct Watermark {
    int trigger_type = 0;
    int value1 = 0;
    int value = 0;
};

struct ConfigState {
    int config_id = 0;
    int activation_count = 0;
    std::vector<Watermark> watermarks;

    int watermark(int trigger_type, int value1) const {
        for (const auto& value : watermarks) {
            if (value.trigger_type == trigger_type && value.value1 == value1) {
                return value.value;
            }
        }
        return 0;
    }

    void add_watermark(int trigger_type, int value1, int amount) {
        for (auto& value : watermarks) {
            if (value.trigger_type == trigger_type && value.value1 == value1) {
                value.value += amount;
                return;
            }
        }
        watermarks.push_back({trigger_type, value1, amount});
    }
};

struct Item {
    int type = 0;
    int inventory_index = 0;
    int item_id = 0;
    int position = 0;
    std::vector<int> config_ids;
    std::vector<int> tags;
    std::vector<int> cells;
    std::vector<ConfigState> config_states;

    int minimum_damage = 0;
    int maximum_damage = 0;
    int damage_type = 1;
    float hit_rate = 0.0F;
    double crit_chance = 0.0;
    double crit_multiplier = 1.5;
    float cooldown = 0.0F;
    float cooldown_progress = 0.0F;
    float cooldown_modifier = 0.0F;
    double stamina_cost = 0.0;
    double damage_bonus = 0.0;
    double final_damage_pct = 0.0;
    double hit_rate_bonus = 0.0;
    std::vector<int> floors;
    std::vector<int> equipment;
    std::vector<Bd2IbSynergyInput> synergies;
    std::vector<Bd2IbReverseSynergyInput> reverse_synergies;
    std::array<int, 4> active_synergy{};
    std::array<int, 4> triggered_synergy{};
    std::array<int, 4> consumed_synergy{};
    int hit_count = 0;
    std::vector<Expiry> expiries;

    int target_unit = -1;
    std::vector<int> floor_units;

    bool has_tag(int tag) const {
        return std::find(tags.begin(), tags.end(), tag) != tags.end();
    }

    ConfigState& state_for(int config_id) {
        for (auto& state : config_states) {
            if (state.config_id == config_id) {
                return state;
            }
        }
        config_states.push_back({config_id, 0, {}});
        return config_states.back();
    }
};

struct Stats {
    int attacks = 0;
    int hits = 0;
    int64_t damage = 0;
    int stamina_misses = 0;
};

class Simulator;

struct Player {
    int side = 0;
    int64_t max_hp = 0;
    int64_t hp = 0;
    double max_stamina = 0.0;
    double stamina = 0.0;
    double stamina_recovery = 0.0;
    float periodic_progress = 0.0F;
    Simulator* engine = nullptr;
    std::vector<std::pair<int, double>> statuses;
    std::unordered_map<int, int> replace_tokens;
    std::vector<Expiry> expiries;
    int token = 0;
    int64_t direct_damage = 0;
    double final_damage_pct = 0.0;
    double crit_chance = 0.0;
    int64_t energy_guard = 0;
    int64_t flat_damage_reduction = 0;
    double invincible_until = -1.0;
    double stunned_until = -1.0;
    std::unordered_map<int, int> bonus_stack_gain;
    int gained_buff_total = 0;
    int inflicted_debuff_total = 0;
    int removed_buff_total = 0;
    std::unordered_map<int, int> gained_buff_by_status;
    std::unordered_map<int, int> inflicted_debuff_by_status;
    std::unordered_map<int, int> removed_buff_by_status;

    double status(int id) const {
        for (const auto& value : statuses) {
            if (value.first == id) {
                return value.second;
            }
        }
        return 0.0;
    }

    void set_status(int id, double amount) {
        for (auto& value : statuses) {
            if (value.first == id) {
                value.second = amount;
                return;
            }
        }
        statuses.emplace_back(id, amount);
    }

    void erase_status(int id) {
        statuses.erase(
            std::remove_if(statuses.begin(), statuses.end(),
                [id](const auto& value) { return value.first == id; }),
            statuses.end());
    }

    double total_buffs() const {
        double result = 0.0;
        for (const auto& value : statuses) {
            if (value.first >= 1000 && value.first < 2000) {
                result += value.second;
            }
        }
        return result;
    }

    double total_debuffs() const {
        double result = 0.0;
        for (const auto& value : statuses) {
            if (value.first >= 2000 && value.first < 3000) {
                result += value.second;
            }
        }
        return result;
    }

    double trigger_status(int id) const {
        if (id != 1000 && id != 2000 && id != 10000 && id != 20000) return status(id);
        const int lower = (id == 1000 || id == 10000) ? 1000 : 2000;
        double result = 0.0;
        for (const auto& value : statuses) {
            if (value.first >= lower && value.first < lower + 1000) {
                result = id < 10000 ? result + static_cast<int>(value.second) : std::max(result, value.second);
            }
        }
        return result;
    }

    double add_status(int id, double amount, double duration = -1.0);
    double remove_status(int id, double amount, bool resist = true, bool notify = true);
    void update(double now);
    bool pay(double cost);
    int64_t heal(int64_t amount, bool apply_modifiers = true);
    int64_t take_damage(int64_t amount, bool bypass_guard = false);
};

struct Team {
    int side = 0;
    Player player;
    std::vector<Item> items;
    std::vector<int> floors;
    std::vector<int> units;
    std::vector<int> equipment;
};

struct EffectContext {
    int source_side = 0;
    int source_item = 0;
    int actor_side = 0;
    int opponent_side = 1;
};

struct Target {
    bool unit = false;
    int side = 0;
    int item = -1;
};

class Simulator {
public:
    Simulator(const Context& context, const Bd2IbBattleInput& input, int32_t seed)
        : context_(context), input_(input), rng_(seed) {
        teams_[0] = build_team(0, input.blue, input.blue_hp);
        teams_[1] = build_team(1, input.red, input.red_hp);
        teams_[0].player.engine = this;
        teams_[1].player.engine = this;
        fatigue_damage_ = context_.fatigue_start_damage;
    }

    Bd2IbBattleResult run() {
        apply_permanent_effects();
        battle_start();
        const float max_seconds = input_.max_seconds > 0.0F
            ? input_.max_seconds
            : 600.0F;
        while (time_ < max_seconds) {
            time_ = static_cast<float>(time_ + kTickSeconds);
            update_fatigue();
            if (finished()) {
                break;
            }
            update_team(0, 1);
            update_team(1, 0);
            if (finished()) {
                break;
            }
        }
        Bd2IbBattleResult result{};
        result.status = BD2_IB_STATUS_OK;
        result.winner = teams_[1].player.hp <= 0 ? 1 :
            (teams_[0].player.hp <= 0 ? 2 : 0);
        result.elapsed_seconds = time_;
        result.rng_draw_count = rng_.draw_count();
        fill_side(result.blue, teams_[0], stats_[0]);
        fill_side(result.red, teams_[1], stats_[1]);
        return result;
    }

    Rng& rng() { return rng_; }
    double time() const { return time_; }

    void reflect_healing_damage(int side, int64_t amount) {
        teams_[static_cast<size_t>(1 - side)].player.take_damage(amount);
    }

    void status_changed(Player&) {
        if (applying_permanent_effects_ || broadcasting_status_change_) {
            return;
        }
        broadcasting_status_change_ = true;
        broadcast_activation(4);
        broadcasting_status_change_ = false;
    }

private:
    const Context& context_;
    const Bd2IbBattleInput& input_;
    Rng rng_;
    std::array<Team, 2> teams_{};
    std::array<Stats, 2> stats_{};
    float time_ = 0.0F;
    int event_depth_ = 0;
    bool broadcasting_status_change_ = false;
    bool in_additional_attack_ = false;
    bool applying_permanent_effects_ = false;
    int fatigue_damage_ = 0;
    int fatigue_tick_count_ = 0;
    bool fatigue_started_ = false;
    float next_fatigue_ = -1.0F;

    static void fill_side(Bd2IbSideResult& out, const Team& team, const Stats& stats) {
        out.hp = static_cast<int32_t>(team.player.hp);
        out.max_hp = static_cast<int32_t>(team.player.max_hp);
        out.energy_guard = static_cast<int32_t>(team.player.energy_guard);
        out.stamina = static_cast<float>(team.player.stamina);
        out.attacks = stats.attacks;
        out.hits = stats.hits;
        out.damage = stats.damage;
        out.stamina_misses = stats.stamina_misses;
    }

    Team build_team(int side, const Bd2IbTeamInput& source, int hp) {
        if (source.item_count < 0 || source.item_count > BD2_IB_MAX_ITEMS_PER_TEAM) {
            throw std::invalid_argument("item count outside native capacity");
        }
        Team team;
        team.side = side;
        team.player.side = side;
        team.player.max_hp = hp;
        team.player.hp = hp;
        team.player.max_stamina = context_.default_stamina;
        team.player.stamina = context_.default_stamina;
        team.player.stamina_recovery = context_.default_stamina_recovery;
        team.items.reserve(static_cast<size_t>(source.item_count));
        for (int index = 0; index < source.item_count; ++index) {
            const auto& row = source.items[index];
            const auto count_valid = [](int count, int capacity) {
                return count >= 0 && count <= capacity;
            };
            if (!count_valid(row.config_count, BD2_IB_MAX_CONFIGS_PER_ITEM) ||
                !count_valid(row.tag_count, BD2_IB_MAX_TAGS_PER_ITEM) ||
                !count_valid(row.cell_count, BD2_IB_MAX_CELLS_PER_ITEM) ||
                !count_valid(row.floor_count, BD2_IB_MAX_FLOORS_PER_UNIT) ||
                !count_valid(row.equipment_count, BD2_IB_MAX_EQUIPMENT_PER_UNIT) ||
                !count_valid(row.synergy_count, BD2_IB_MAX_SYNERGY_PER_UNIT) ||
                !count_valid(row.reverse_synergy_count,
                    BD2_IB_MAX_REVERSE_SYNERGY_PER_UNIT) ||
                !count_valid(row.floor_unit_count, BD2_IB_MAX_CELLS_PER_ITEM)) {
                throw std::invalid_argument("native item count exceeds ABI capacity");
            }
            Item item;
            item.type = row.item_type;
            item.inventory_index = row.inventory_index;
            item.item_id = row.item_id;
            item.position = row.position;
            item.config_ids.assign(row.config_ids, row.config_ids + row.config_count);
            item.tags.assign(row.tags, row.tags + row.tag_count);
            item.cells.assign(row.cells, row.cells + row.cell_count);
            item.minimum_damage = row.minimum_damage;
            item.maximum_damage = row.maximum_damage;
            item.damage_type = row.damage_type;
            item.hit_rate = row.hit_rate;
            item.crit_chance = row.critical_chance;
            item.crit_multiplier = row.critical_multiplier;
            item.cooldown = row.cooldown;
            item.stamina_cost = row.stamina_cost;
            item.floors.assign(
                row.floor_item_indices,
                row.floor_item_indices + row.floor_count);
            item.equipment.assign(
                row.equipment_item_indices,
                row.equipment_item_indices + row.equipment_count);
            item.synergies.assign(row.synergies, row.synergies + row.synergy_count);
            item.reverse_synergies.assign(
                row.reverse_synergies,
                row.reverse_synergies + row.reverse_synergy_count);
            for (const auto& synergy : item.synergies) {
                if (synergy.slot_type >= 1 && synergy.slot_type <= 3) {
                    ++item.active_synergy[static_cast<size_t>(synergy.slot_type)];
                }
            }
            item.target_unit = row.target_unit_item_index;
            item.floor_units.assign(
                row.floor_unit_item_indices,
                row.floor_unit_item_indices + row.floor_unit_count);
            team.items.push_back(std::move(item));
            if (row.item_type == BD2_IB_ITEM_FLOOR) {
                team.floors.push_back(index);
            } else if (row.item_type == BD2_IB_ITEM_UNIT) {
                team.units.push_back(index);
            } else if (row.item_type == BD2_IB_ITEM_EQUIPMENT) {
                team.equipment.push_back(index);
            } else {
                throw std::invalid_argument("unknown native item type");
            }
        }
        const auto valid_index = [&team](int index) {
            return index >= 0 && index < static_cast<int>(team.items.size());
        };
        for (const auto& row : team.items) {
            for (int index : row.floors) if (!valid_index(index)) throw std::invalid_argument("invalid floor index");
            for (int index : row.equipment) if (!valid_index(index)) throw std::invalid_argument("invalid equipment index");
            for (const auto& slot : row.synergies) if (!valid_index(slot.target_item_index)) throw std::invalid_argument("invalid synergy target");
            for (const auto& reverse : row.reverse_synergies) if (!valid_index(reverse.source_unit_item_index)) throw std::invalid_argument("invalid reverse synergy source");
            if (row.target_unit >= 0 && !valid_index(row.target_unit)) throw std::invalid_argument("invalid equipment target");
            for (int index : row.floor_units) if (!valid_index(index)) throw std::invalid_argument("invalid floor unit index");
        }
        return team;
    }

    bool finished() const {
        return teams_[0].player.hp <= 0 || teams_[1].player.hp <= 0;
    }

    EffectContext make_context(int source_side, int source_item,
                               int actor_side, int opponent_side) const {
        return {source_side, source_item, actor_side, opponent_side};
    }

    Item& item(const EffectContext& context) {
        return teams_[static_cast<size_t>(context.source_side)]
            .items[static_cast<size_t>(context.source_item)];
    }

    void broadcast_activation(int timing) {
        if (event_depth_ > 12) {
            return;
        }
        ++event_depth_;
        for (int side = 0; side < 2; ++side) {
            for (size_t index = 0; index < teams_[static_cast<size_t>(side)].items.size(); ++index) {
                activate(side, static_cast<int>(index), timing, side, 1 - side);
            }
        }
        --event_depth_;
    }

    void hp_changed() { broadcast_activation(3); }

    void activate(int source_side, int source_item, int timing,
                  int actor_side, int opponent_side) {
        auto context = make_context(source_side, source_item, actor_side, opponent_side);
        auto& source = item(context);
        const std::vector<int> config_ids = source.config_ids;
        for (int config_id : config_ids) {
            const auto found = context_.configs.find(config_id);
            if (found == context_.configs.end() || found->second.effect_type == 1) {
                continue;
            }
            const auto config = found->second;
            auto& state = source.state_for(config_id);
            const int limit = config.result_count;
            if (limit != 999 && state.activation_count >= limit) {
                continue;
            }
            if (rng_.range_int(0, 10000) >= config.result_ratio) {
                continue;
            }
            int count = trigger_count(config, state, timing, context);
            if (count <= 0) {
                continue;
            }
            if (limit != 999) {
                count = std::min(count, limit - state.activation_count);
            }
            state.activation_count += count;
            consume_trigger(config, state, count, context);
            for (int index = 0; index < count; ++index) {
                apply_effect_group(config.effect_group_id, config.result_type, context);
            }
        }
    }

    int trigger_count(const Bd2IbConfigInput& config, ConfigState& state,
                      int timing, EffectContext& context) {
        if (config.trigger_id == 0) {
            return 0;
        }
        const auto found = context_.triggers.find(config.trigger_id);
        if (found == context_.triggers.end()) {
            return 0;
        }
        const auto& trigger = found->second;
        if (trigger.timing_type != 0 && trigger.timing_type != timing) {
            return 0;
        }
        if (trigger.term_count <= 0) {
            return 1;
        }
        std::array<int, BD2_IB_MAX_TRIGGER_TERMS> results{};
        for (int index = 0; index < trigger.term_count; ++index) {
            results[static_cast<size_t>(index)] = condition_count(
                trigger.term_type[index], trigger.term_value1[index],
                trigger.term_value2[index], state, context);
        }
        if (trigger.logical_type == 1) {
            int result = std::numeric_limits<int>::max();
            for (int index = 0; index < trigger.term_count; ++index) {
                if (results[static_cast<size_t>(index)] <= 0) {
                    return 0;
                }
                result = std::min(result, results[static_cast<size_t>(index)]);
            }
            return result;
        }
        int result = 0;
        for (int index = 0; index < trigger.term_count; ++index) {
            result = std::max(result, results[static_cast<size_t>(index)]);
        }
        return result;
    }

    int condition_count(int trigger_type, int v1, int v2, ConfigState& state,
                        EffectContext& context) {
        auto& source = item(context);
        auto& actor = teams_[static_cast<size_t>(context.actor_side)].player;
        auto& opponent = teams_[static_cast<size_t>(context.opponent_side)].player;
        const bool unit = source.type == BD2_IB_ITEM_UNIT;
        switch (trigger_type) {
        case 0: return 1;
        case 1: return actor.trigger_status(v1) >= v2 ? 1 : 0;
        case 2: return opponent.trigger_status(v1) >= v2 ? 1 : 0;
        case 3: return actor.gained_buff_total >= v1 ? 1 : 0;
        case 4: return opponent.gained_buff_total >= v1 ? 1 : 0;
        case 5: return static_cast<double>(actor.hp) /
            static_cast<double>(std::max<int64_t>(1, actor.max_hp)) * 10000.0 >= v1 ? 1 : 0;
        case 6: return static_cast<double>(opponent.hp) /
            static_cast<double>(std::max<int64_t>(1, opponent.max_hp)) * 10000.0 >= v1 ? 1 : 0;
        case 7: return static_cast<double>(actor.hp) /
            static_cast<double>(std::max<int64_t>(1, actor.max_hp)) * 10000.0 <= v1 ? 1 : 0;
        case 8: return static_cast<double>(opponent.hp) /
            static_cast<double>(std::max<int64_t>(1, opponent.max_hp)) * 10000.0 <= v1 ? 1 : 0;
        case 10: {
            int total = 0;
            for (const auto& config_state : source.config_states) {
                total += config_state.activation_count;
            }
            return total >= v1 ? 1 : 0;
        }
        case 11: case 12: case 13:
            return unit && source.triggered_synergy[static_cast<size_t>(trigger_type - 10)] >= v1 ? 1 : 0;
        case 20: return unit && source.hit_count >= v1 ? 1 : 0;
        case 21: case 22: case 23:
            return unit && source.consumed_synergy[static_cast<size_t>(trigger_type - 20)] >= v1 ? 1 : 0;
        case 25: case 26: case 27:
            return unit && source.active_synergy[static_cast<size_t>(trigger_type - 24)] >= v1 ? 1 : 0;
        case 29:
            return unit && (source.active_synergy[1] + source.active_synergy[2] +
                source.active_synergy[3]) >= v1 ? 1 : 0;
        case 30: case 31: case 32: {
            int current = 0;
            const int aggregate = trigger_type == 31 ? 2000 : 1000;
            if (v1 == 0 || v1 == aggregate) {
                current = trigger_type == 30 ? actor.removed_buff_total :
                    (trigger_type == 31 ? actor.inflicted_debuff_total : actor.gained_buff_total);
            } else {
                const auto& values = trigger_type == 30 ? actor.removed_buff_by_status :
                    (trigger_type == 31 ? actor.inflicted_debuff_by_status : actor.gained_buff_by_status);
                const auto found = values.find(v1);
                current = found == values.end() ? 0 : found->second;
            }
            const int threshold = std::max(1, v2);
            return std::max(0, (current - state.watermark(trigger_type, v1)) / threshold);
        }
        default: return 0;
        }
    }

    void consume_trigger(const Bd2IbConfigInput& config, ConfigState& state,
                         int count, EffectContext& context) {
        const auto found = context_.triggers.find(config.trigger_id);
        if (found == context_.triggers.end()) {
            return;
        }
        auto& source = item(context);
        const auto& trigger = found->second;
        for (int index = 0; index < trigger.term_count; ++index) {
            const int type = trigger.term_type[index];
            const int v1 = trigger.term_value1[index];
            const int v2 = trigger.term_value2[index];
            if (type >= 30 && type <= 32) {
                state.add_watermark(type, v1, count * std::max(1, v2));
            } else if (type >= 11 && type <= 13 && source.type == BD2_IB_ITEM_UNIT && v1 > 0) {
                auto& value = source.triggered_synergy[static_cast<size_t>(type - 10)];
                if (value >= v1) value = 0;
            } else if (type == 20 && source.type == BD2_IB_ITEM_UNIT && v1 > 0) {
                if (source.hit_count >= v1) source.hit_count = 0;
            } else if (type >= 21 && type <= 23 && source.type == BD2_IB_ITEM_UNIT && v1 > 0) {
                auto& value = source.consumed_synergy[static_cast<size_t>(type - 20)];
                if (value >= v1) value = 0;
            }
        }
    }

    void apply_effect_group(int group_id, int result_type, EffectContext& context) {
        if (group_id == 0) {
            return;
        }
        const auto found = context_.effects.find(group_id);
        if (found == context_.effects.end() || found->second.empty()) {
            return;
        }
        const auto& rows = found->second;
        if (result_type == 0) {
            for (const auto& row : rows) {
                apply_effect_row(row, context);
            }
        } else {
            apply_effect_row(rows[static_cast<size_t>(rng_.range_int(0, static_cast<int>(rows.size())))], context);
        }
    }

    void apply_effect_row(const Bd2IbEffectInput& row, EffectContext& context) {
        const int count = effect_count(row.effect_count, context);
        if (count <= 0) {
            return;
        }
        const auto selected = targets(row, context);
        for (const auto& target : selected) {
            apply_effect(row, target, count, context);
        }
    }

    int effect_count(int count_type, EffectContext& context) {
        const auto& source = item(context);
        const bool unit = source.type == BD2_IB_ITEM_UNIT;
        const auto& actor = teams_[static_cast<size_t>(context.actor_side)].player;
        const auto& opponent = teams_[static_cast<size_t>(context.opponent_side)].player;
        if (count_type == 0) return 1;
        if (count_type >= 1 && count_type <= 3) {
            return unit ? source.active_synergy[static_cast<size_t>(count_type)] : 0;
        }
        if (count_type == 4) return source.type == BD2_IB_ITEM_FLOOR ? static_cast<int>(source.floor_units.size()) : 0;
        if (count_type == 1000) return static_cast<int>(actor.total_buffs());
        if (count_type >= 1001 && count_type <= 1007) return static_cast<int>(actor.status(count_type));
        if (count_type == 2000) return static_cast<int>(actor.total_debuffs());
        if (count_type >= 2001 && count_type <= 2004) return static_cast<int>(actor.status(count_type));
        if (count_type == 3000) return static_cast<int>(opponent.total_buffs());
        if (count_type >= 3001 && count_type <= 3007) return static_cast<int>(opponent.status(count_type - 2000));
        if (count_type == 4000) return static_cast<int>(opponent.total_debuffs());
        if (count_type >= 4001 && count_type <= 4004) return static_cast<int>(opponent.status(count_type - 2000));
        return 1;
    }

    std::vector<Target> targets(const Bd2IbEffectInput& row, EffectContext& context) {
        const int type = row.effect_target_type;
        auto& source = item(context);
        if (type == 1) return {{false, context.actor_side, -1}};
        if (type == 2) return {{false, context.opponent_side, -1}};
        if (type == 3) {
            return source.type == BD2_IB_ITEM_UNIT
                ? std::vector<Target>{{true, context.source_side, context.source_item}}
                : std::vector<Target>{{false, context.actor_side, -1}};
        }
        if (type >= 4 && type <= 6) {
            const int synergy_type = type - 3;
            std::vector<Target> result;
            auto& actor_team = teams_[static_cast<size_t>(context.actor_side)];
            for (int unit_index : actor_team.units) {
                const auto& unit = actor_team.items[static_cast<size_t>(unit_index)];
                for (const auto& slot : unit.synergies) {
                    if (slot.slot_type != synergy_type) continue;
                    const int target_index = slot.target_item_index;
                    if (target_index < 0 || actor_team.items[static_cast<size_t>(target_index)].type != BD2_IB_ITEM_UNIT) continue;
                    if (std::none_of(result.begin(), result.end(),
                        [target_index](const Target& value) { return value.unit && value.item == target_index; })) {
                        result.push_back({true, context.actor_side, target_index});
                    }
                }
            }
            return result;
        }
        if (type == 8 && source.type == BD2_IB_ITEM_FLOOR) {
            std::vector<Target> result;
            for (int index : source.floor_units) result.push_back({true, context.source_side, index});
            return result;
        }
        if (type == 9 || type == 10) {
            const int side = type == 9 ? context.actor_side : context.opponent_side;
            std::vector<Target> result;
            for (int index : teams_[static_cast<size_t>(side)].units) {
                const auto& unit = teams_[static_cast<size_t>(side)].items[static_cast<size_t>(index)];
                if (row.effect_target_tag_id == 0 || unit.has_tag(row.effect_target_tag_id)) {
                    result.push_back({true, side, index});
                }
            }
            return result;
        }
        if (type == 11 && source.type == BD2_IB_ITEM_FLOOR) {
            std::vector<Target> result;
            for (int index : source.floor_units) {
                const auto& unit = teams_[static_cast<size_t>(context.source_side)].items[static_cast<size_t>(index)];
                if (row.effect_target_tag_id == 0 || unit.has_tag(row.effect_target_tag_id)) {
                    result.push_back({true, context.source_side, index});
                }
            }
            return result;
        }
        if (type == 12 && source.type == BD2_IB_ITEM_EQUIPMENT && source.target_unit >= 0) {
            return {{true, context.source_side, source.target_unit}};
        }
        return {{false, context.actor_side, -1}};
    }

    static double damage_effect_value(int subtype, int value, int count) {
        return subtype == 0 || subtype == 1
            ? value * 0.1
            : subtype * 0.1 * count;
    }

    std::vector<int> status_targets(int subtype, Player& player, bool debuff, bool adding) {
        std::vector<int> base;
        if (debuff) base.assign(kDebuffTypes.begin(), kDebuffTypes.end());
        else base.assign(kBuffTypes.begin(), kBuffTypes.end());
        const int aggregate = debuff ? 2000 : 1000;
        const int strongest = debuff ? 20000 : 10000;
        const int all_code = debuff ? 200000 : 100000;
        std::vector<int> active;
        for (const auto& status : player.statuses) {
            if (status.second > 0 && status.first >= aggregate && status.first < aggregate + 1000) {
                active.push_back(status.first);
            }
        }
        // The current client sorts CollectHeldBuffs before selecting/removing.
        std::sort(active.begin(), active.end());
        if (subtype == aggregate) {
            auto& pool = adding ? base : active;
            return pool.empty() ? std::vector<int>{} :
                std::vector<int>{pool[static_cast<size_t>(rng_.range_int(0, static_cast<int>(pool.size())))]};
        }
        if (subtype == strongest) {
            if (active.empty()) {
                return base.empty() ? std::vector<int>{} :
                    std::vector<int>{base[static_cast<size_t>(rng_.range_int(0, static_cast<int>(base.size())))]};
            }
            double maximum = 0.0;
            for (int id : active) maximum = std::max(maximum, player.status(id));
            std::vector<int> pool;
            for (int id : active) if (player.status(id) == maximum) pool.push_back(id);
            std::sort(pool.begin(), pool.end());
            if (pool.size() == 1) return pool;
            return {pool[static_cast<size_t>(rng_.range_int(0, static_cast<int>(pool.size())))]};
        }
        if (subtype == all_code) return adding ? base : active;
        return {subtype};
    }

    void apply_effect(const Bd2IbEffectInput& row, const Target& target,
                      int count, EffectContext& context) {
        const int effect_type = row.effect_type;
        const int subtype = row.effect_sub_type;
        const int value = row.effect_value * count;
        const double duration = row.effect_value2 > 0 ? static_cast<float>(row.effect_value2) * 0.01F : -1.0;
        if (target.unit && (effect_type == 1 || effect_type == 2 || effect_type == 3 ||
            effect_type == 9 || effect_type == 10 || effect_type == 11 ||
            effect_type == 22 || effect_type == 23 || effect_type == 26 || effect_type == 27 || effect_type == 29)) {
            apply_unit_effect(effect_type, subtype, value, duration, count,
                target.side, target.item, context);
            return;
        }
        Player& player = target.unit
            ? teams_[static_cast<size_t>(target.side)].player
            : teams_[static_cast<size_t>(target.side)].player;
        Player& actor = teams_[static_cast<size_t>(context.actor_side)].player;
        Player& opponent = teams_[static_cast<size_t>(context.opponent_side)].player;
        if (effect_type == 1 || effect_type == 22 || effect_type == 23) {
            const int64_t applied = unity_round(damage_effect_value(subtype, value, count));
            if (subtype == 1) player.direct_damage = applied;
            else player.direct_damage += applied;
            if (effect_type == 1 && duration > 0) {
                player.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::DirectDamage, 0, static_cast<double>(applied), 0});
            }
        } else if (effect_type == 4) {
            for (int id : status_targets(subtype, player, false, true)) {
                const int bonus = player.bonus_stack_gain[id];
                player.add_status(id, value + value * bonus, duration);
            }
        } else if (effect_type == 5) {
            for (int id : status_targets(subtype, player, false, false)) {
                const double removed = player.remove_status(id, value);
                if (&player == &opponent) {
                    actor.removed_buff_total += static_cast<int>(removed);
                    actor.removed_buff_by_status[id] += static_cast<int>(removed);
                }
            }
        } else if (effect_type == 6) {
            for (int id : status_targets(subtype, player, true, true)) {
                const double added = player.add_status(id, value, duration);
                if (&player == &opponent) {
                    actor.inflicted_debuff_total += static_cast<int>(added);
                    actor.inflicted_debuff_by_status[id] += static_cast<int>(added);
                }
            }
        } else if (effect_type == 7) {
            for (int id : status_targets(subtype, player, true, false)) player.remove_status(id, value, false);
        } else if (effect_type == 12) {
            player.take_damage(unity_round(value * 0.1), true);
        } else if (effect_type == 13) {
            player.stamina = std::max(0.0, player.stamina - value / 1000.0);
        } else if (effect_type == 14) {
            player.stamina = std::min(player.max_stamina, player.stamina + value / 1000.0);
        } else if (effect_type == 15) {
            player.heal(value);
        } else if (effect_type == 16) {
            for (int id : status_targets(subtype, player, false, false)) {
                const double removed = player.remove_status(id, value);
                if (removed <= 0) continue;
                if (&player == &opponent) {
                    actor.removed_buff_total += static_cast<int>(removed);
                    actor.removed_buff_by_status[id] += static_cast<int>(removed);
                }
                const int bonus = actor.bonus_stack_gain[id];
                actor.add_status(id, removed + removed * bonus, duration);
            }
        } else if (effect_type == 17) {
            player.max_hp += value;
            player.heal(value, false);
        } else if (effect_type == 18) {
            player.energy_guard += subtype == 1
                ? unity_round(player.max_hp * value / 100.0) : value;
        } else if (effect_type == 19) {
            const int64_t removed = subtype == 1
                ? unity_round(player.max_hp * value / 100.0) : value;
            player.energy_guard = std::max<int64_t>(0, player.energy_guard - removed);
        } else if (effect_type == 21) {
            player.flat_damage_reduction += value;
        } else if (effect_type == 24) {
            if (rng_.next_float() >= player.status(kStunResist) / 100.0) {
                player.stunned_until = std::max(player.stunned_until, static_cast<double>(static_cast<float>(time_ + static_cast<float>(value) * 0.01F)));
                const int other_side = 1 - target.side;
                for (size_t index = 0; index < teams_[static_cast<size_t>(other_side)].items.size(); ++index) {
                    activate(other_side, static_cast<int>(index), 15, other_side, target.side);
                }
            }
        } else if (effect_type == 25) {
            for (int id : status_targets(subtype, player, false, false)) {
                if (player.status(id) > 0) player.remove_status(id, value);
            }
        } else if (effect_type == 26) {
            const double applied = value / 100.0;
            player.final_damage_pct += applied;
            if (duration > 0) player.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::FinalDamage, 0, applied, 0});
        } else if (effect_type == 27) {
            const double applied = value / 100.0;
            player.crit_chance += applied;
            if (duration > 0) player.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::CritChance, 0, applied, 0});
        } else if (effect_type == 28) {
            player.add_status(subtype, value / 100.0, duration);
        } else if (effect_type == 30) {
            player.bonus_stack_gain[subtype] += value;
            if (duration > 0) player.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::BonusGain, subtype, static_cast<double>(value), 0});
        } else if (effect_type == 31) {
            player.energy_guard += static_cast<int64_t>(std::floor(player.max_hp * value / 100.0));
        } else if (effect_type == 32) {
            player.invincible_until = value > 0
                ? static_cast<double>(static_cast<float>(time_ + static_cast<float>(value) * 0.01F))
                : std::numeric_limits<double>::infinity();
        }
    }

    void apply_unit_effect(int effect_type, int subtype, int value,
                           double duration, int count, int side,
                           int item_index, EffectContext& context) {
        auto& unit = teams_[static_cast<size_t>(side)].items[static_cast<size_t>(item_index)];
        if (effect_type == 1 || effect_type == 22 || effect_type == 23) {
            const double applied = damage_effect_value(subtype, value, count);
            unit.damage_bonus = subtype == 1 ? applied : unit.damage_bonus + applied;
            if (effect_type == 1 && duration > 0) unit.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::UnitDamageBonus, 0, applied, 0});
        } else if (effect_type == 2 || effect_type == 3) {
            float applied = static_cast<float>(static_cast<float>(value) / 100.0F);
            if (effect_type == 2) applied = -applied;
            unit.cooldown_modifier = subtype == 1
                ? applied
                : static_cast<float>(unit.cooldown_modifier + applied);
            if (duration > 0) unit.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::UnitCooldownModifier, 0, applied, 0});
        } else if (effect_type == 9) {
            for (int index = 0; index < count; ++index) additional_attack(side, item_index, context.actor_side, context.opponent_side);
        } else if (effect_type == 10) {
            unit.stamina_cost += value / 1000.0;
        } else if (effect_type == 11) {
            unit.stamina_cost = std::max(0.0, unit.stamina_cost - value / 1000.0);
        } else if (effect_type == 26) {
            unit.final_damage_pct += value / 100.0;
        } else if (effect_type == 27) {
            const double applied = value / 100.0;
            unit.crit_chance += applied;
            if (duration > 0) unit.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::UnitCritChance, 0, applied, 0});
        } else if (effect_type == 29) {
            const double applied = value / 100.0;
            unit.hit_rate_bonus += applied;
            if (duration > 0) unit.expiries.push_back({static_cast<float>(time_ + duration), ExpiryKind::UnitHitRate, 0, applied, 0});
        }
    }

    void apply_permanent_effects() {
        applying_permanent_effects_ = true;
        for (int side = 0; side < 2; ++side) {
            for (size_t index = 0; index < teams_[static_cast<size_t>(side)].items.size(); ++index) {
                const auto config_ids = teams_[static_cast<size_t>(side)].items[index].config_ids;
                for (int config_id : config_ids) {
                    const auto found = context_.configs.find(config_id);
                    if (found == context_.configs.end() || found->second.effect_type != 1) continue;
                    auto effect_context = make_context(side, static_cast<int>(index), side, 1 - side);
                    apply_effect_group(found->second.effect_group_id, found->second.result_type, effect_context);
                }
            }
        }
        applying_permanent_effects_ = false;
    }

    void battle_start() {
        for (int side = 0; side < 2; ++side) {
            for (size_t index = 0; index < teams_[static_cast<size_t>(side)].items.size(); ++index) {
                activate(side, static_cast<int>(index), 5, side, 1 - side);
            }
        }
    }

    void expire_unit(Item& unit) {
        std::vector<Expiry> remaining;
        for (const auto& expiry : unit.expiries) {
            if (expiry.at > time_ + 1e-9) {
                remaining.push_back(expiry);
                continue;
            }
            if (expiry.kind == ExpiryKind::UnitDamageBonus) unit.damage_bonus -= expiry.amount;
            else if (expiry.kind == ExpiryKind::UnitCooldownModifier) unit.cooldown_modifier -= static_cast<float>(expiry.amount);
            else if (expiry.kind == ExpiryKind::UnitCritChance) unit.crit_chance -= expiry.amount;
            else if (expiry.kind == ExpiryKind::UnitHitRate) unit.hit_rate_bonus -= expiry.amount;
        }
        unit.expiries.swap(remaining);
    }

    void update_team(int side, int opponent_side) {
        auto& team = teams_[static_cast<size_t>(side)];
        auto& opponent = teams_[static_cast<size_t>(opponent_side)];
        for (int unit_index : team.units) {
            auto& unit = team.items[static_cast<size_t>(unit_index)];
            // Native stun blocks cooldown progress; expiration is applied by
            // the player after all units have updated for this tick.
            if (unit.cooldown <= 0 || team.player.stunned_until >= time_) continue;
            float speed = static_cast<float>(
                static_cast<float>(team.player.status(kOverheat) - team.player.status(kCold)) * 0.02F);
            speed = static_cast<float>(speed + unit.cooldown_modifier);
            float effective = speed > 0
                ? static_cast<float>(static_cast<double>(unit.cooldown) / (1.0 + static_cast<double>(speed)))
                : static_cast<float>(static_cast<double>(unit.cooldown) * (1.0 - static_cast<double>(speed)));
            effective = std::max(static_cast<float>(unit.cooldown * 0.1F), effective);
            unit.cooldown_progress = static_cast<float>(
                static_cast<double>(unit.cooldown_progress) + static_cast<double>(kTickSeconds) / static_cast<double>(effective));
            if (unit.cooldown_progress < 1.0F) continue;
            if (team.player.stunned_until > time_) continue;
            if (team.player.pay(unit.stamina_cost)) cycle_attack(side, unit_index, opponent_side);
            else ++stats_[static_cast<size_t>(side)].stamina_misses;
            unit.cooldown_progress = static_cast<float>(unit.cooldown_progress - 1.0F);
        }
        team.player.update(time_);
        for (int unit_index : team.units) expire_unit(team.items[static_cast<size_t>(unit_index)]);
        (void)opponent;
    }

    void cycle_attack(int side, int unit_index, int opponent_side) {
        auto& team = teams_[static_cast<size_t>(side)];
        auto& unit = team.items[static_cast<size_t>(unit_index)];
        activate(side, unit_index, 1, side, opponent_side);
        for (int equipment_index : unit.equipment) activate(side, equipment_index, 13, side, opponent_side);
        for (int floor_index : unit.floors) activate(side, floor_index, 12, side, opponent_side);
        std::vector<int> seen;
        for (const auto& slot : unit.synergies) {
            if (!slot.notify_target) continue;
            if (std::find(seen.begin(), seen.end(), slot.target_item_index) != seen.end()) continue;
            seen.push_back(slot.target_item_index);
            activate(side, slot.target_item_index, 7, side, opponent_side);
        }
        const bool hit = perform_attack(side, unit_index, opponent_side);
        for (const auto& reverse : unit.reverse_synergies) {
            auto& source = team.items[static_cast<size_t>(reverse.source_unit_item_index)];
            if (reverse.slot_type >= 1 && reverse.slot_type <= 3) {
                ++source.triggered_synergy[static_cast<size_t>(reverse.slot_type)];
                if (hit) ++source.consumed_synergy[static_cast<size_t>(reverse.slot_type)];
            }
            activate(side, reverse.source_unit_item_index, 7, side, opponent_side);
        }
    }

    void additional_attack(int unit_side, int unit_index, int actor_side, int opponent_side) {
        if (in_additional_attack_) return;
        in_additional_attack_ = true;
        perform_attack(actor_side, unit_index, opponent_side, unit_side);
        in_additional_attack_ = false;
    }

    bool perform_attack(int actor_side, int unit_index, int opponent_side,
                        int unit_side = -1) {
        if (unit_side < 0) unit_side = actor_side;
        auto& unit_team = teams_[static_cast<size_t>(unit_side)];
        auto& unit = unit_team.items[static_cast<size_t>(unit_index)];
        auto& actor = teams_[static_cast<size_t>(actor_side)].player;
        auto& opponent = teams_[static_cast<size_t>(opponent_side)].player;
        if (unit.minimum_damage == 0 || unit.maximum_damage == 0) {
            ++stats_[static_cast<size_t>(actor_side)].attacks;
            return false;
        }
        activate(unit_side, unit_index, 10, actor_side, opponent_side);
        activate(unit_side, unit_index, unit.damage_type != 2 ? 8 : 9, actor_side, opponent_side);
        float damage_value = rng_.range_float(
            static_cast<float>(unit.minimum_damage),
            static_cast<float>(unit.maximum_damage));
        damage_value = static_cast<float>(damage_value + static_cast<float>(unit.damage_bonus));
        bool critical = rng_.next_float() < unit.crit_chance + actor.crit_chance;
        if (critical) damage_value = static_cast<float>(damage_value * static_cast<float>(unit.crit_multiplier));
        if (unit.final_damage_pct != 0) {
            damage_value = static_cast<float>(damage_value * static_cast<float>(1.0 + unit.final_damage_pct));
        }
        int64_t damage = unity_round(damage_value);
        damage += unity_round(actor.status(kStrength));
        damage += actor.direct_damage;
        if (actor.final_damage_pct != 0) damage = unity_round(damage * (1.0 + actor.final_damage_pct));
        double hit_chance = unit.hit_rate + unit.hit_rate_bonus;
        hit_chance += (actor.status(kFocus) - actor.status(kDarkness)) * 0.05;
        const bool hit = unit.maximum_damage <= 0 || rng_.next_float() < hit_chance;
        ++stats_[static_cast<size_t>(actor_side)].attacks;
        if (!hit) return false;
        const double crit_resist = opponent.status(kCritResist);
        if (critical && crit_resist > 0 && rng_.next_float() < crit_resist / 100.0) {
            critical = false;
            damage = unity_round(damage / unit.crit_multiplier);
        }
        activate(unit_side, unit_index, 11, actor_side, opponent_side);
        for (size_t index = 0; index < teams_[static_cast<size_t>(opponent_side)].items.size(); ++index) {
            activate(opponent_side, static_cast<int>(index), 10, opponent_side, actor_side);
            activate(opponent_side, static_cast<int>(index), unit.damage_type != 2 ? 8 : 9, opponent_side, actor_side);
        }
        const int64_t dealt = opponent.take_damage(damage);
        hp_changed();
        ++unit.hit_count;
        ++stats_[static_cast<size_t>(actor_side)].hits;
        stats_[static_cast<size_t>(actor_side)].damage += dealt;
        activate(unit_side, unit_index, 2, actor_side, opponent_side);
        for (int equipment_index : unit.equipment) activate(unit_side, equipment_index, 14, actor_side, opponent_side);
        if (unit.damage_type != 2) {
            const double drain = actor.status(kDrain);
            if (drain > 0) actor.heal(unity_round(std::min<double>(drain, dealt)));
            const double counter = opponent.status(kCounter);
            if (counter > 0) actor.take_damage(unity_round(std::min<double>(counter, dealt)));
        }
        return true;
    }

    void fatigue_tick() {
        teams_[0].player.take_damage(fatigue_damage_);
        teams_[1].player.take_damage(fatigue_damage_);
        ++fatigue_tick_count_;
        fatigue_damage_ = static_cast<int>(std::ceil(
            context_.fatigue_start_damage *
            std::pow(context_.fatigue_growth_factor, fatigue_tick_count_)));
        next_fatigue_ = static_cast<float>(next_fatigue_ + context_.fatigue_interval_time);
    }

    void update_fatigue() {
        if (!fatigue_started_ && time_ >= context_.fatigue_start_time) {
            fatigue_started_ = true;
            next_fatigue_ = static_cast<float>(time_ + context_.fatigue_interval_time);
        }
        if (fatigue_started_ && time_ >= next_fatigue_) fatigue_tick();
    }
};

double Player::add_status(int id, double amount, double duration) {
    if (amount <= 0) return 0.0;
    const bool debuff = id >= 2000 && id < 3000;
    if (debuff) {
        const double resist = status(kDebuffResist);
        if (resist > 0 && engine->rng().next_float() < resist / 100.0) return 0.0;
    } else {
        const double block = status(kBuffBlockChance);
        if (block > 0 && engine->rng().next_float() < block / 100.0) return 0.0;
    }
    double changed = amount;
    if (id < 1000) {
        const double previous = status(id);
        set_status(id, amount);
        ++token;
        replace_tokens[id] = token;
        if (duration > 0) expiries.push_back({static_cast<float>(engine->time() + duration), ExpiryKind::ReplaceStatus, id, amount, token});
        changed = amount - previous;
    } else {
        set_status(id, status(id) + amount);
        expiries.push_back({duration > 0 ? static_cast<float>(engine->time() + duration) : std::numeric_limits<double>::infinity(), ExpiryKind::StackStatus, id, amount, 0});
    }
    if (id >= 1000 && id < 2000) {
        gained_buff_total += static_cast<int>(amount);
        gained_buff_by_status[id] += static_cast<int>(amount);
    }
    if (id >= 1000) engine->status_changed(*this);
    return changed;
}

double Player::remove_status(int id, double amount, bool resist, bool notify) {
    const double current = status(id);
    if (current <= 0 || amount <= 0) return 0.0;
    const double resistance = status(kBuffRemoveResist);
    if (resist && resistance > 0 && engine->rng().next_float() < resistance / 100.0) return 0.0;
    const double removed = std::min(current, amount);
    if (notify && id >= 1000) {
        double pending = removed;
        for (auto& entry : expiries) {
            if (entry.kind != ExpiryKind::StackStatus || entry.key != id) continue;
            const double consumed = std::min(entry.amount, pending);
            entry.amount -= consumed;
            pending -= consumed;
            if (pending <= 0) break;
        }
        expiries.erase(std::remove_if(expiries.begin(), expiries.end(), [](const Expiry& e) {
            return e.kind == ExpiryKind::StackStatus && e.amount <= 0;
        }), expiries.end());
    }
    const double remaining = current - removed;
    if (remaining > 0) set_status(id, remaining);
    else erase_status(id);
    if (id >= 1000 && notify) engine->status_changed(*this);
    return removed;
}

void Player::update(double now) {
    std::vector<Expiry> remaining;
    std::vector<Expiry> expired;
    for (const auto& expiry : expiries) {
        if (expiry.at <= now + 1e-9) expired.push_back(expiry);
        else remaining.push_back(expiry);
    }
    expiries.swap(remaining);
    for (const auto& expiry : expired) {
        if (expiry.kind == ExpiryKind::StackStatus) {
            // Expiration only dirties native status storage. Explicit removal
            // broadcasts stack-change triggers; natural expiration does not.
            remove_status(expiry.key, expiry.amount, false, false);
        } else if (expiry.kind == ExpiryKind::ReplaceStatus) {
            const auto found = replace_tokens.find(expiry.key);
            if (found != replace_tokens.end() && found->second == expiry.token) {
                erase_status(expiry.key);
                replace_tokens.erase(found);
            }
        } else if (expiry.kind == ExpiryKind::DirectDamage) direct_damage -= static_cast<int64_t>(expiry.amount);
        else if (expiry.kind == ExpiryKind::FinalDamage) final_damage_pct -= expiry.amount;
        else if (expiry.kind == ExpiryKind::CritChance) crit_chance -= expiry.amount;
        else if (expiry.kind == ExpiryKind::BonusGain) bonus_stack_gain[expiry.key] -= static_cast<int>(expiry.amount);
    }
    periodic_progress = static_cast<float>(periodic_progress + kTickSeconds);
    if (periodic_progress >= 1.0F) {
        periodic_progress = static_cast<float>(periodic_progress - 1.0F);
        const double regen = status(kRegen);
        if (regen > 0) heal(unity_round(regen));
        const double poison = status(kPoison);
        if (poison > 0) take_damage(unity_round(poison));
    }
    float recovery = static_cast<float>(stamina_recovery / 100.0);
    recovery = static_cast<float>(recovery * static_cast<float>(1.0 + status(kStaminaRegen) / 100.0));
    stamina = static_cast<float>(std::min(max_stamina, static_cast<double>(static_cast<float>(stamina + recovery))));
}

bool Player::pay(double cost) {
    const double actual = cost * (1.0 + status(kBleed) * 0.02);
    if (stamina + 1e-9 < actual) return false;
    stamina -= actual;
    return true;
}

int64_t Player::heal(int64_t amount, bool apply_modifiers) {
    if (apply_modifiers) {
        amount = unity_round(amount *
            (1.0 + status(kHealAmplify) / 100.0 - status(kHealReduction) / 100.0));
    }
    const int64_t before = hp;
    hp = std::min(max_hp, hp + amount);
    if (apply_modifiers && status(kFireDamage) > 0) {
        engine->reflect_healing_damage(
            side, unity_round(amount * status(kFireDamage) / 100.0));
    }
    return hp - before;
}

int64_t Player::take_damage(int64_t amount, bool bypass_guard) {
    if (invincible_until >= 0 && engine->time() < invincible_until) return 0;
    amount = unity_round(amount * (1.0 - status(kDamageReduction) / 100.0));
    amount = unity_round(amount * (1.0 + status(kIncomingDamageIncrease) / 100.0));
    if (flat_damage_reduction > 0) {
        amount = std::max<int64_t>(0, amount - flat_damage_reduction);
        flat_damage_reduction = 0;
    }
    const int64_t result = amount;
    if (energy_guard > 0 && !bypass_guard) {
        const int64_t absorbed = std::min(energy_guard, amount);
        energy_guard -= absorbed;
        amount -= absorbed;
    }
    hp -= amount;
    return result;
}

}  // namespace

extern "C" {

int32_t bd2_ib_native_abi_version(void) {
    return BD2_IB_NATIVE_ABI_VERSION;
}

void* bd2_ib_context_create(const Bd2IbTableInput* input) {
    if (input == nullptr || input->config_count < 0 || input->effect_count < 0 ||
        input->trigger_count < 0 ||
        (input->config_count > 0 && input->configs == nullptr) ||
        (input->effect_count > 0 && input->effects == nullptr) ||
        (input->trigger_count > 0 && input->triggers == nullptr)) {
        return nullptr;
    }
    try {
        auto* context = new Context();
        context->default_stamina = input->default_stamina;
        context->default_stamina_recovery = input->default_stamina_recovery;
        context->fatigue_start_time = input->fatigue_start_time;
        context->fatigue_interval_time = input->fatigue_interval_time;
        context->fatigue_start_damage = input->fatigue_start_damage;
        context->fatigue_growth_factor = input->fatigue_growth_factor;
        for (int index = 0; index < input->config_count; ++index) {
            context->configs[input->configs[index].id] = input->configs[index];
        }
        for (int index = 0; index < input->effect_count; ++index) {
            context->effects[input->effects[index].group_id].push_back(input->effects[index]);
        }
        for (auto& group : context->effects) {
            std::sort(group.second.begin(), group.second.end(),
                [](const auto& left, const auto& right) { return left.id < right.id; });
        }
        for (int index = 0; index < input->trigger_count; ++index) {
            if (input->triggers[index].term_count < 0 ||
                input->triggers[index].term_count > BD2_IB_MAX_TRIGGER_TERMS) {
                delete context;
                return nullptr;
            }
            context->triggers[input->triggers[index].id] = input->triggers[index];
        }
        return context;
    } catch (...) {
        return nullptr;
    }
}

void bd2_ib_context_destroy(void* context) {
    delete static_cast<Context*>(context);
}

int32_t bd2_ib_simulate_batch(void* context, const Bd2IbBattleInput* battle,
                              const int32_t* seeds, int32_t seed_count,
                              Bd2IbBattleResult* results) {
    if (context == nullptr || battle == nullptr || seeds == nullptr ||
        results == nullptr || seed_count < 0) {
        return BD2_IB_STATUS_INVALID_INPUT;
    }
    std::atomic<int32_t> next_index{0};
    std::atomic<int32_t> batch_status{BD2_IB_STATUS_OK};
    const auto run_one = [&]() {
        while (true) {
            const int32_t index = next_index.fetch_add(1);
            if (index >= seed_count) {
                return;
            }
            try {
                Simulator simulator(
                    *static_cast<Context*>(context), *battle, seeds[index]);
                results[index] = simulator.run();
            } catch (const std::invalid_argument&) {
                results[index] = {};
                results[index].status = BD2_IB_STATUS_INVALID_INPUT;
                int32_t expected = BD2_IB_STATUS_OK;
                batch_status.compare_exchange_strong(
                    expected, BD2_IB_STATUS_INVALID_INPUT);
            } catch (...) {
                results[index] = {};
                results[index].status = BD2_IB_STATUS_INTERNAL_ERROR;
                int32_t expected = BD2_IB_STATUS_OK;
                batch_status.compare_exchange_strong(
                    expected, BD2_IB_STATUS_INTERNAL_ERROR);
            }
        }
    };
    const unsigned int hardware = std::max(1U, std::thread::hardware_concurrency());
    const int32_t worker_count = std::min<int32_t>(
        seed_count, static_cast<int32_t>(std::min(16U, hardware)));
    if (worker_count <= 1) {
        run_one();
    } else {
        std::vector<std::thread> workers;
        workers.reserve(static_cast<size_t>(worker_count));
        for (int32_t worker = 0; worker < worker_count; ++worker) {
            workers.emplace_back(run_one);
        }
        for (auto& worker : workers) {
            worker.join();
        }
    }
    return batch_status.load();
}

}  // extern "C"
