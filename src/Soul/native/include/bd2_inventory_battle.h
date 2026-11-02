#pragma once

#include <stdint.h>

#if defined(_WIN32)
#define BD2_IB_EXPORT __declspec(dllexport)
#else
#define BD2_IB_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum {
    BD2_IB_NATIVE_ABI_VERSION = 1,
    BD2_IB_MAX_CONFIGS_PER_ITEM = 4,
    BD2_IB_MAX_TAGS_PER_ITEM = 20,
    BD2_IB_MAX_CELLS_PER_ITEM = 9,
    BD2_IB_MAX_FLOORS_PER_UNIT = 9,
    BD2_IB_MAX_EQUIPMENT_PER_UNIT = 4,
    BD2_IB_MAX_SYNERGY_PER_UNIT = 20,
    BD2_IB_MAX_REVERSE_SYNERGY_PER_UNIT = 20,
    BD2_IB_MAX_ITEMS_PER_TEAM = 128,
    BD2_IB_MAX_TRIGGER_TERMS = 2,
};

enum {
    BD2_IB_STATUS_OK = 0,
    BD2_IB_STATUS_INVALID_INPUT = 1,
    BD2_IB_STATUS_UNSUPPORTED = 2,
    BD2_IB_STATUS_INTERNAL_ERROR = 3,
};

enum {
    BD2_IB_ITEM_FLOOR = 0,
    BD2_IB_ITEM_UNIT = 1,
    BD2_IB_ITEM_EQUIPMENT = 2,
};

typedef struct Bd2IbConfigInput {
    int32_t id;
    int32_t effect_type;
    int32_t effect_group_id;
    int32_t result_count;
    int32_t result_ratio;
    int32_t result_type;
    int32_t trigger_id;
    int32_t reserved;
} Bd2IbConfigInput;

typedef struct Bd2IbEffectInput {
    int32_t group_id;
    int32_t id;
    int32_t effect_count;
    int32_t effect_sub_type;
    int32_t effect_target_tag_id;
    int32_t effect_target_type;
    int32_t effect_type;
    int32_t effect_value;
    int32_t effect_value2;
    int32_t reserved;
} Bd2IbEffectInput;

typedef struct Bd2IbTriggerInput {
    int32_t id;
    int32_t timing_type;
    int32_t logical_type;
    int32_t term_count;
    int32_t term_type[BD2_IB_MAX_TRIGGER_TERMS];
    int32_t term_value1[BD2_IB_MAX_TRIGGER_TERMS];
    int32_t term_value2[BD2_IB_MAX_TRIGGER_TERMS];
} Bd2IbTriggerInput;

typedef struct Bd2IbTableInput {
    float default_stamina;
    float default_stamina_recovery;
    float fatigue_start_time;
    float fatigue_interval_time;
    int32_t fatigue_start_damage;
    float fatigue_growth_factor;
    int32_t config_count;
    int32_t effect_count;
    int32_t trigger_count;
    int32_t reserved;
    const Bd2IbConfigInput* configs;
    const Bd2IbEffectInput* effects;
    const Bd2IbTriggerInput* triggers;
} Bd2IbTableInput;

typedef struct Bd2IbSynergyInput {
    int32_t slot_type;
    int32_t target_item_index;
    int32_t notify_target;
    int32_t reserved;
} Bd2IbSynergyInput;

typedef struct Bd2IbReverseSynergyInput {
    int32_t source_unit_item_index;
    int32_t slot_type;
} Bd2IbReverseSynergyInput;

typedef struct Bd2IbItemInput {
    int32_t item_type;
    int32_t inventory_index;
    int32_t item_id;
    int32_t position;
    int32_t config_count;
    int32_t config_ids[BD2_IB_MAX_CONFIGS_PER_ITEM];
    int32_t tag_count;
    int32_t tags[BD2_IB_MAX_TAGS_PER_ITEM];
    int32_t cell_count;
    int32_t cells[BD2_IB_MAX_CELLS_PER_ITEM];

    int32_t minimum_damage;
    int32_t maximum_damage;
    int32_t damage_type;
    float hit_rate;
    float critical_chance;
    float critical_multiplier;
    float cooldown;
    float stamina_cost;

    int32_t floor_count;
    int32_t floor_item_indices[BD2_IB_MAX_FLOORS_PER_UNIT];
    int32_t equipment_count;
    int32_t equipment_item_indices[BD2_IB_MAX_EQUIPMENT_PER_UNIT];
    int32_t synergy_count;
    Bd2IbSynergyInput synergies[BD2_IB_MAX_SYNERGY_PER_UNIT];
    int32_t reverse_synergy_count;
    Bd2IbReverseSynergyInput reverse_synergies[BD2_IB_MAX_REVERSE_SYNERGY_PER_UNIT];

    int32_t target_unit_item_index;
    int32_t floor_unit_count;
    int32_t floor_unit_item_indices[BD2_IB_MAX_CELLS_PER_ITEM];
    int32_t reserved[4];
} Bd2IbItemInput;

typedef struct Bd2IbTeamInput {
    int32_t item_count;
    int32_t reserved[3];
    Bd2IbItemInput items[BD2_IB_MAX_ITEMS_PER_TEAM];
} Bd2IbTeamInput;

typedef struct Bd2IbBattleInput {
    int32_t blue_hp;
    int32_t red_hp;
    float max_seconds;
    int32_t reserved;
    Bd2IbTeamInput blue;
    Bd2IbTeamInput red;
} Bd2IbBattleInput;

typedef struct Bd2IbSideResult {
    int32_t hp;
    int32_t max_hp;
    int32_t energy_guard;
    float stamina;
    int32_t attacks;
    int32_t hits;
    int64_t damage;
    int32_t stamina_misses;
    int32_t reserved;
} Bd2IbSideResult;

typedef struct Bd2IbBattleResult {
    int32_t status;
    int32_t winner;
    float elapsed_seconds;
    int32_t reserved;
    uint64_t rng_draw_count;
    Bd2IbSideResult blue;
    Bd2IbSideResult red;
} Bd2IbBattleResult;

BD2_IB_EXPORT int32_t bd2_ib_native_abi_version(void);
BD2_IB_EXPORT void* bd2_ib_context_create(const Bd2IbTableInput* input);
BD2_IB_EXPORT void bd2_ib_context_destroy(void* context);
BD2_IB_EXPORT int32_t bd2_ib_simulate_batch(
    void* context,
    const Bd2IbBattleInput* battle,
    const int32_t* seeds,
    int32_t seed_count,
    Bd2IbBattleResult* results
);

#ifdef __cplusplus
}
#endif
