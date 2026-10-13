#include <stdint.h>
#include <stdio.h>

#include "trace_sha256.h"
#include "trace_config_parser.h"
#include "trace_address_math.h"
#include "trace_patch_builder.h"
#include "trace_stub_builder.h"

static int failures;

static void require(int condition, const char *message) {
    if (condition) return;
    fprintf(stderr, "FAIL: %s\n", message);
    ++failures;
}

static int bytes_equal(const uint8_t *left, const uint8_t *right, uint32_t size) {
    uint32_t index;
    for (index = 0u; index < size; ++index) {
        if (left[index] != right[index]) return 0;
    }
    return 1;
}

static void require_digest(const uint8_t actual[32], const uint8_t expected[32], const char *message) {
    uint32_t index;
    if (bytes_equal(actual, expected, 32u)) return;
    fprintf(stderr, "FAIL: %s\n  actual:   ", message);
    for (index = 0u; index < 32u; ++index) fprintf(stderr, "%02x", actual[index]);
    fprintf(stderr, "\n  expected: ");
    for (index = 0u; index < 32u; ++index) fprintf(stderr, "%02x", expected[index]);
    fputc('\n', stderr);
    ++failures;
}

static uint32_t read_u32(const uint8_t *source) {
    return (uint32_t)source[0] |
           ((uint32_t)source[1] << 8u) |
           ((uint32_t)source[2] << 16u) |
           ((uint32_t)source[3] << 24u);
}

static uint32_t relative_destination(const uint8_t *instruction, uint32_t instruction_address) {
    return instruction_address + 5u + read_u32(instruction + 1u);
}

static void test_sha256(void) {
    static const uint8_t empty_digest[32] = {
        0xe3u, 0xb0u, 0xc4u, 0x42u, 0x98u, 0xfcu, 0x1cu, 0x14u,
        0x9au, 0xfbu, 0xf4u, 0xc8u, 0x99u, 0x6fu, 0xb9u, 0x24u,
        0x27u, 0xaeu, 0x41u, 0xe4u, 0x64u, 0x9bu, 0x93u, 0x4cu,
        0xa4u, 0x95u, 0x99u, 0x1bu, 0x78u, 0x52u, 0xb8u, 0x55u,
    };
    static const uint8_t abc_digest[32] = {
        0xbau, 0x78u, 0x16u, 0xbfu, 0x8fu, 0x01u, 0xcfu, 0xeau,
        0x41u, 0x41u, 0x40u, 0xdeu, 0x5du, 0xaeu, 0x22u, 0x23u,
        0xb0u, 0x03u, 0x61u, 0xa3u, 0x96u, 0x17u, 0x7au, 0x9cu,
        0xb4u, 0x10u, 0xffu, 0x61u, 0xf2u, 0x00u, 0x15u, 0xadu,
    };
    static const uint8_t million_a_digest[32] = {
        0xcdu, 0xc7u, 0x6eu, 0x5cu, 0x99u, 0x14u, 0xfbu, 0x92u,
        0x81u, 0xa1u, 0xc7u, 0xe2u, 0x84u, 0xd7u, 0x3eu, 0x67u,
        0xf1u, 0x80u, 0x9au, 0x48u, 0xa4u, 0x97u, 0x20u, 0x0eu,
        0x04u, 0x6du, 0x39u, 0xccu, 0xc7u, 0x11u, 0x2cu, 0xd0u,
    };
    TraceSha256Context context;
    uint8_t digest[32];
    uint8_t chunk[1000];
    uint32_t index;

    trace_sha256_init(&context);
    trace_sha256_finish(&context, digest);
    require_digest(digest, empty_digest, "SHA-256 empty vector");

    trace_sha256_init(&context);
    trace_sha256_update(&context, (const uint8_t *)"abc", 3u);
    trace_sha256_finish(&context, digest);
    require_digest(digest, abc_digest, "SHA-256 abc vector");

    trace_sha256_init(&context);
    trace_sha256_update(&context, (const uint8_t *)"a", 1u);
    trace_sha256_update(&context, (const uint8_t *)"b", 1u);
    trace_sha256_update(&context, (const uint8_t *)"c", 1u);
    trace_sha256_finish(&context, digest);
    require_digest(digest, abc_digest, "SHA-256 streaming vector");

    for (index = 0u; index < (uint32_t)sizeof(chunk); ++index) chunk[index] = (uint8_t)'a';
    trace_sha256_init(&context);
    for (index = 0u; index < 1000u; ++index) {
        trace_sha256_update(&context, chunk, (uint32_t)sizeof(chunk));
    }
    trace_sha256_finish(&context, digest);
    require_digest(digest, million_a_digest, "SHA-256 million-a vector");
}

static void test_config_u32_parser(void) {
    uint32_t value = 0u;
    require(trace_parse_u32("0", &value) && value == 0u, "config parser accepts zero");
    require(trace_parse_u32("4294967295", &value) && value == 0xffffffffu,
            "config parser accepts UINT32_MAX");
    require(!trace_parse_u32("4294967296", &value), "config parser rejects UINT32_MAX plus one");
    require(!trace_parse_u32("18446744073709552616", &value),
            "config parser rejects a uint64-wrapping decimal");
    require(!trace_parse_u32("+1", &value) && !trace_parse_u32("1x", &value),
            "config parser rejects non-decimal syntax");
    require(!trace_parse_u32("", &value) && !trace_parse_u32((const char *)0, &value) &&
                !trace_parse_u32("1", (uint32_t *)0),
            "config parser rejects missing inputs");
}

static void test_u32_spans(void) {
    uint64_t end = 0u;
    require(trace_checked_u32_span(0u, 1u, &end) && end == 1u,
            "u32 span accepts the first byte");
    require(trace_checked_u32_span(0xffffffffu, 1u, &end) && end == UINT64_C(0x100000000),
            "u32 span accepts a final byte at UINT32_MAX");
    require(trace_checked_u32_span(0xfffffff0u, 16u, &end) && end == UINT64_C(0x100000000),
            "u32 span accepts an exact top-ending range");
    require(!trace_checked_u32_span(0xfffffff0u, 17u, &end),
            "u32 span rejects wrap past the address space");
    require(!trace_checked_u32_span(1u, 0u, &end) &&
                !trace_checked_u32_span(1u, 1u, (uint64_t *)0),
            "u32 span rejects empty or missing outputs");
}

static void initialize_stub_spec(TraceStubSpec *spec) {
    uint32_t index;
    uint8_t *raw = (uint8_t *)spec;
    for (index = 0u; index < (uint32_t)sizeof(*spec); ++index) raw[index] = 0u;
    spec->stub_address = 0x10000000u;
    spec->capture_active_address = 0x20000000u;
    spec->trace_enter_address = 0x30000000u;
    spec->hook_id = 0x11223344u;
    spec->target_address = 0x00401000u;
    spec->stolen_len = 5u;
    for (index = 0u; index < spec->stolen_len; ++index) spec->stolen[index] = 0x90u;
}

static void test_stub_without_relocation(void) {
    static const uint8_t fxsave[4] = {0x0fu, 0xaeu, 0x04u, 0x24u};
    static const uint8_t fxrstor[4] = {0x0fu, 0xaeu, 0x0cu, 0x24u};
    TraceStubSpec spec;
    uint8_t code[TRACE_STUB_CAPACITY];
    uint32_t size;
    uint32_t index;

    initialize_stub_spec(&spec);
    size = trace_build_entry_stub(code, &spec);
    require(size == 58u, "plain stub size");
    require(code[0] == 0x9cu, "stub saves EFLAGS before activity check");
    require(code[1] == 0x83u && code[2] == 0x3du && read_u32(code + 3u) == spec.capture_active_address,
            "stub reads exact capture-active address");
    require(code[8] == 0x74u && 10u + code[9] == 47u && code[47] == 0x9du,
            "inactive branch lands on popfd");
    require(code[10] == 0x60u && code[46] == 0x61u, "stub balances pushad and popad");
    require(bytes_equal(code + 22u, fxsave, 4u), "stub emits FXSAVE");
    require(bytes_equal(code + 40u, fxrstor, 4u), "stub emits FXRSTOR");
    {
        int32_t stack_from_fxsave = 0;
        require(code[26] == 0x56u, "stub pushes snapshot after FXSAVE");
        stack_from_fxsave -= 4;
        require(code[27] == 0x68u, "stub pushes hook id after snapshot");
        stack_from_fxsave -= 4;
        require(code[32] == 0xe8u, "stub calls the cdecl trace entry");
        stack_from_fxsave -= 4;
        stack_from_fxsave += 4; /* near RET pops the call's return address */
        require(code[37] == 0x83u && code[38] == 0xc4u && code[39] == 0x08u,
                "stub caller removes both cdecl arguments");
        stack_from_fxsave += 8;
        require(stack_from_fxsave == 0, "FXRSTOR uses the exact FXSAVE stack address");
    }
    require(code[27] == 0x68u && read_u32(code + 28u) == spec.hook_id, "stub pushes exact hook id");
    require(code[32] == 0xe8u &&
                relative_destination(code + 32u, spec.stub_address + 32u) == spec.trace_enter_address,
            "stub trace call preserves rel32 destination");
    for (index = 48u; index < 53u; ++index) require(code[index] == 0x90u, "plain stolen bytes copied");
    require(code[53] == 0xe9u &&
                relative_destination(code + 53u, spec.stub_address + 53u) == spec.target_address + spec.stolen_len,
            "plain stub returns after stolen span");
}

static void test_atomic_patch_word(void) {
    static const uint8_t original[TRACE_ATOMIC_PATCH_BYTES] = {
        0x55u, 0x8bu, 0xecu, 0x83u, 0xecu, 0x10u, 0x53u, 0x56u,
    };
    uint64_t patched = trace_build_atomic_jump_word(
        trace_patch_read_u64(original),
        0x00401000u,
        0x10000000u
    );
    uint8_t bytes[TRACE_ATOMIC_PATCH_BYTES];
    uint32_t index;
    uint32_t displacement;
    for (index = 0u; index < TRACE_ATOMIC_PATCH_BYTES; ++index) {
        bytes[index] = (uint8_t)(patched >> (index * 8u));
    }
    displacement = (uint32_t)bytes[1] |
                   (uint32_t)bytes[2] << 8u |
                   (uint32_t)bytes[3] << 16u |
                   (uint32_t)bytes[4] << 24u;
    require(bytes[0] == 0xe9u, "atomic patch emits one near jump");
    require(0x00401005u + displacement == 0x10000000u, "atomic patch jump reaches the stub");
    require(bytes[5] == original[5] && bytes[6] == original[6] && bytes[7] == original[7],
            "atomic patch preserves the trailing three bytes");
    require(trace_rebase_preferred_address(0x00409e60u, 0x00400000u, 0x00600000u) == 0x00609e60u,
            "preferred relocation targets are rebased to the loaded image");
}

static void test_rel32_modulo_reachability(void) {
    uint8_t instruction[5];
    trace_stub_emit_relative(instruction, 0xfffffff0u, 0xe8u, 0x00000010u);
    require(instruction[0] == 0xe8u &&
                trace_stub_relative_destination(instruction, 0xfffffff0u) == 0x00000010u,
            "rel32 reaches forward across the 32-bit address-space boundary");
    trace_stub_emit_relative(instruction, 0x00000010u, 0xe9u, 0xfffffff0u);
    require(instruction[0] == 0xe9u &&
                trace_stub_relative_destination(instruction, 0x00000010u) == 0xfffffff0u,
            "rel32 reaches backward across the 32-bit address-space boundary");
}

static void test_stub_with_relocated_call(void) {
    TraceStubSpec spec;
    uint8_t code[TRACE_STUB_CAPACITY];
    uint32_t size;

    initialize_stub_spec(&spec);
    spec.stolen_len = 6u;
    spec.stolen[0] = 0xe8u;
    spec.stolen[1] = 0xaau;
    spec.stolen[2] = 0xbbu;
    spec.stolen[3] = 0xccu;
    spec.stolen[4] = 0xddu;
    spec.stolen[5] = 0x90u;
    spec.relocation_kind = TRACE_STUB_RELOCATION_CALL_REL32;
    spec.relocation_offset = 0u;
    spec.relocation_target = 0x00500000u;
    size = trace_build_entry_stub(code, &spec);
    require(size == 59u, "relocated-call stub size");
    require(code[48] == 0xe8u &&
                relative_destination(code + 48u, spec.stub_address + 48u) == spec.relocation_target,
            "stolen call is relocated to original destination");
    require(code[53] == 0x90u, "bytes after relocated call are preserved");
    require(code[54] == 0xe9u &&
                relative_destination(code + 54u, spec.stub_address + 54u) == spec.target_address + spec.stolen_len,
            "relocated stub returns after stolen span");

    spec.stolen[0] = 0x90u;
    require(trace_build_entry_stub(code, &spec) == 0u, "relocation rejects a non-call opcode");
    initialize_stub_spec(&spec);
    spec.relocation_kind = 99u;
    require(trace_build_entry_stub(code, &spec) == 0u, "stub rejects unknown relocation kind");
    initialize_stub_spec(&spec);
    spec.stolen_len = 4u;
    require(trace_build_entry_stub(code, &spec) == 0u, "stub rejects a short stolen span");
}

int main(void) {
    test_sha256();
    test_config_u32_parser();
    test_u32_spans();
    test_atomic_patch_word();
    test_rel32_modulo_reachability();
    test_stub_without_relocation();
    test_stub_with_relocated_call();
    if (failures != 0) {
        fprintf(stderr, "%d native contract test(s) failed\n", failures);
        return 1;
    }
    puts("native-contract: PASS");
    return 0;
}
