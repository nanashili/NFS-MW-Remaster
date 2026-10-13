#ifndef NFSMW_AI_TRACE_PATCH_BUILDER_H
#define NFSMW_AI_TRACE_PATCH_BUILDER_H

#include <stdint.h>

#define TRACE_ATOMIC_PATCH_BYTES 8u

static uint32_t trace_rebase_preferred_address(
    uint32_t preferred_address,
    uint32_t preferred_base,
    uint32_t actual_base
) {
    return actual_base + (preferred_address - preferred_base);
}

static uint64_t trace_patch_read_u64(const uint8_t bytes[TRACE_ATOMIC_PATCH_BYTES]) {
    uint64_t value = 0u;
    uint32_t index;
    for (index = 0u; index < TRACE_ATOMIC_PATCH_BYTES; ++index) {
        value |= (uint64_t)bytes[index] << (index * 8u);
    }
    return value;
}

static uint64_t trace_build_atomic_jump_word(
    uint64_t original_word,
    uint32_t target_address,
    uint32_t stub_address
) {
    uint8_t bytes[TRACE_ATOMIC_PATCH_BYTES];
    uint32_t displacement = stub_address - (target_address + 5u);
    uint32_t index;
    for (index = 0u; index < TRACE_ATOMIC_PATCH_BYTES; ++index) {
        bytes[index] = (uint8_t)(original_word >> (index * 8u));
    }
    bytes[0] = 0xe9u;
    bytes[1] = (uint8_t)displacement;
    bytes[2] = (uint8_t)(displacement >> 8u);
    bytes[3] = (uint8_t)(displacement >> 16u);
    bytes[4] = (uint8_t)(displacement >> 24u);
    return trace_patch_read_u64(bytes);
}

#endif
