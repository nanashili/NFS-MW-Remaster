#ifndef NFSMW_AI_TRACE_STUB_BUILDER_H
#define NFSMW_AI_TRACE_STUB_BUILDER_H

#include <stdint.h>

#define TRACE_STUB_CAPACITY 128u
#define TRACE_STUB_RELOCATION_NONE 0u
#define TRACE_STUB_RELOCATION_CALL_REL32 1u

typedef struct TraceStubSpec {
    uint32_t stub_address;
    uint32_t capture_active_address;
    uint32_t trace_enter_address;
    uint32_t hook_id;
    uint32_t target_address;
    uint8_t stolen[16];
    uint32_t stolen_len;
    uint32_t relocation_kind;
    uint32_t relocation_offset;
    uint32_t relocation_target;
} TraceStubSpec;

static void trace_stub_write_u32(uint8_t *destination, uint32_t value) {
    destination[0] = (uint8_t)value;
    destination[1] = (uint8_t)(value >> 8u);
    destination[2] = (uint8_t)(value >> 16u);
    destination[3] = (uint8_t)(value >> 24u);
}

static uint32_t trace_stub_read_u32(const uint8_t *source) {
    return (uint32_t)source[0] |
           ((uint32_t)source[1] << 8u) |
           ((uint32_t)source[2] << 16u) |
           ((uint32_t)source[3] << 24u);
}

/* In 32-bit mode EIP arithmetic is modulo 2^32, so rel32 can reach every
   address in the 32-bit address space in either wrap direction. */
static uint32_t trace_stub_relative_destination(const uint8_t *instruction, uint32_t instruction_address) {
    return instruction_address + 5u + trace_stub_read_u32(instruction + 1u);
}

static void trace_stub_emit_relative(
    uint8_t *instruction,
    uint32_t instruction_address,
    uint8_t opcode,
    uint32_t destination
) {
    instruction[0] = opcode;
    trace_stub_write_u32(instruction + 1u, destination - (instruction_address + 5u));
}

static uint32_t trace_build_entry_stub(uint8_t code[TRACE_STUB_CAPACITY], const TraceStubSpec *spec) {
    uint32_t at = 0u;
    uint32_t inactive_jump;
    uint32_t restore_flags;
    uint32_t stolen_at;
    uint32_t index;
    if (code == (uint8_t *)0 || spec == (const TraceStubSpec *)0 ||
        spec->stolen_len < 5u || spec->stolen_len > (uint32_t)sizeof(spec->stolen)) return 0u;
    for (index = 0u; index < TRACE_STUB_CAPACITY; ++index) code[index] = 0u;
    code[at++] = 0x9cu;
    code[at++] = 0x83u; code[at++] = 0x3du;
    trace_stub_write_u32(code + at, spec->capture_active_address); at += 4u;
    code[at++] = 0x00u;
    inactive_jump = at;
    code[at++] = 0x74u; code[at++] = 0x00u;
    code[at++] = 0x60u;
    code[at++] = 0x89u; code[at++] = 0xe6u;
    code[at++] = 0x81u; code[at++] = 0xecu;
    trace_stub_write_u32(code + at, 528u); at += 4u;
    code[at++] = 0x83u; code[at++] = 0xe4u; code[at++] = 0xf0u;
    code[at++] = 0x0fu; code[at++] = 0xaeu; code[at++] = 0x04u; code[at++] = 0x24u;
    code[at++] = 0x56u;
    code[at++] = 0x68u; trace_stub_write_u32(code + at, spec->hook_id); at += 4u;
    trace_stub_emit_relative(code + at, spec->stub_address + at, 0xe8u, spec->trace_enter_address); at += 5u;
    code[at++] = 0x83u; code[at++] = 0xc4u; code[at++] = 0x08u;
    code[at++] = 0x0fu; code[at++] = 0xaeu; code[at++] = 0x0cu; code[at++] = 0x24u;
    code[at++] = 0x89u; code[at++] = 0xf4u;
    code[at++] = 0x61u;
    restore_flags = at;
    code[at++] = 0x9du;
    if (restore_flags - (inactive_jump + 2u) > 0x7fu || at + spec->stolen_len + 5u > TRACE_STUB_CAPACITY) return 0u;
    code[inactive_jump + 1u] = (uint8_t)(restore_flags - (inactive_jump + 2u));
    stolen_at = at;
    for (index = 0u; index < spec->stolen_len; ++index) code[at++] = spec->stolen[index];
    if (spec->relocation_kind == TRACE_STUB_RELOCATION_CALL_REL32) {
        uint32_t relocated_at;
        if (spec->relocation_offset + 5u > spec->stolen_len ||
            spec->stolen[spec->relocation_offset] != 0xe8u) return 0u;
        relocated_at = stolen_at + spec->relocation_offset;
        trace_stub_emit_relative(
            code + relocated_at,
            spec->stub_address + relocated_at,
            0xe8u,
            spec->relocation_target
        );
    } else if (spec->relocation_kind != TRACE_STUB_RELOCATION_NONE) {
        return 0u;
    }
    trace_stub_emit_relative(code + at, spec->stub_address + at, 0xe9u, spec->target_address + spec->stolen_len);
    at += 5u;
    return at;
}

#endif
