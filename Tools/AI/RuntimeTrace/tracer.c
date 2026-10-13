#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <stdint.h>

#include "trace_sha256.h"
#include "trace_config_parser.h"
#include "trace_address_math.h"
#include "trace_patch_builder.h"
#include "trace_stub_builder.h"

#if !defined(_M_IX86) && !defined(__i386__)
#error NFSMWAITrace must be compiled for 32-bit x86.
#endif

#define TRACE_VERSION "0.4.2"
#define TRACE_SCHEMA 4u
#define TRACE_HEADER_SIZE 32768u
#define TRACE_HEADER_CORE_SIZE 1024u
#define TRACE_MANIFEST_ENTRY_SIZE 320u
#define TRACE_RECORD_SIZE 1024u
#define TRACE_RECORD_META_SIZE 256u
#define TRACE_HOOK_COUNT 64u
#define TRACE_HEADER_PADDING_SIZE (TRACE_HEADER_SIZE - TRACE_HEADER_CORE_SIZE - TRACE_HOOK_COUNT * TRACE_MANIFEST_ENTRY_SIZE)
#define TRACE_REGION_COUNT 3u
#define TRACE_REGION_BYTES 256u
#define TRACE_QUEUE_CAPACITY 2048u
#define TRACE_MAX_READ_CALLS 12u
#define TRACE_MAX_STOLEN_BYTES 16u
#define TRACE_MAX_INSTRUCTIONS 8u
#define TRACE_MAX_SUSPENDED_THREADS 256u
#define TRACE_THREAD_SCAN_ROUNDS 64u
#define TRACE_THREAD_STABLE_SCANS 4u
#define TRACE_WRITE_RETRIES 3u
#define TRACE_EVENT_MAGIC 0x33545645u /* EVT3 */

#if defined(__GNUC__) || defined(__clang__)
static const char kTraceVersionIdentity[] __attribute__((used)) = TRACE_VERSION;
#else
static const char kTraceVersionIdentity[] = TRACE_VERSION;
#endif

#if defined(__GNUC__) || defined(__clang__)
#define TRACE_CALL __attribute__((cdecl, force_align_arg_pointer, noinline))
#else
#define TRACE_CALL __cdecl __declspec(noinline)
#endif

enum TraceEventKind {
    TRACE_EVENT_ENTRY = 1,
    TRACE_EVENT_CHECKPOINT = 2,
    TRACE_EVENT_TERMINAL = 3,
};

enum TraceTerminalReason {
    TRACE_TERMINAL_NONE = 0,
    TRACE_TERMINAL_LIMIT = 1,
    TRACE_TERMINAL_PROCESS_DETACH = 2,
    TRACE_TERMINAL_WRITE_FAILURE = 3,
    TRACE_TERMINAL_CALL_INDEX_WRAP = 4,
    TRACE_TERMINAL_STOP_REQUEST = 5,
};

enum TraceActivationState {
    TRACE_STATE_STARTING = 1,
    TRACE_STATE_VALIDATED = 2,
    TRACE_STATE_PATCHING = 3,
    TRACE_STATE_ACTIVE = 4,
    TRACE_STATE_STOPPING = 5,
    TRACE_STATE_CLOSED = 6,
    TRACE_STATE_FAILED = 7,
};

enum TraceHookFlags {
    TRACE_HOOK_STATIC = 1u,
};

enum TraceRegionSource {
    TRACE_SOURCE_NONE = 0,
    TRACE_SOURCE_NORMALIZED_THIS = 1,
    TRACE_SOURCE_RAW_THIS = 2,
    TRACE_SOURCE_ARG0 = 16,
    TRACE_SOURCE_ARG7 = 23,
    TRACE_SOURCE_ABSOLUTE = 32,
};

enum TraceRelocationKind {
    TRACE_RELOCATION_NONE = 0,
    TRACE_RELOCATION_CALL_REL32 = 1,
};

enum TraceHeaderFlags {
    TRACE_HEADER_ENTRY_ONLY = 1u,
    TRACE_HEADER_FXSAVE = 2u,
    TRACE_HEADER_PROFILE_PRESET = 4u,
    TRACE_HEADER_ORDERED_FIFO = 8u,
};

enum TraceEventFlags {
    TRACE_EVENT_RETURN_ADDRESS_VALID = 1u,
    TRACE_EVENT_NORMALIZED_THIS_VALID = 2u,
    TRACE_EVENT_REGION_PARTIAL = 4u,
    TRACE_EVENT_READ_BUDGET_EXCEEDED = 8u,
    TRACE_EVENT_SYNTHETIC_PROBE = 16u,
    TRACE_EVENT_PROBE_ASSISTED = 32u,
};

#define ZBWARE_RELEASE_PUT_STRING_ADDRESS 0x0045cc70u
#define ZBWARE_LAUNCH_WINDOWS_ADDRESS 0x0045cd70u
#define ZBWARE_HAS_TICKER_EXPIRED_ADDRESS 0x0045cf30u
#define ZBWARE_MEMORY_TRACING_ADDRESS 0x0090e674u
#define ZBWARE_TRACE_NEW_POOL_HOOK_ID 55u
#define ZBWARE_PROBE_MEMORY_TIMEOUT_MS 30000u

typedef struct TraceRegionDefinition {
    uint32_t source;
    int32_t offset;
    uint32_t length;
    const char *label;
} TraceRegionDefinition;

typedef struct TraceHookDefinition {
    uint32_t id;
    uint32_t address;
    uint32_t rva;
    uint32_t profile_mask;
    uint32_t default_divisor;
    uint32_t arg_count;
    int32_t this_adjust;
    uint32_t flags;
    uint32_t return_kind;
    uint32_t arg_type_bits;
    uint32_t relocation_kind;
    uint32_t relocation_offset;
    uint32_t relocation_target;
    uint8_t stolen[TRACE_MAX_STOLEN_BYTES];
    uint32_t stolen_len;
    uint32_t instruction_count;
    uint8_t instruction_lengths[TRACE_MAX_INSTRUCTIONS];
    const char *name;
    const char *config_key;
    const char *recovery;
    TraceRegionDefinition regions[TRACE_REGION_COUNT];
} TraceHookDefinition;

typedef struct TraceProfileDefinition {
    uint32_t id;
    uint32_t mask;
    const char *name;
} TraceProfileDefinition;

#include "generated_hook_catalogue.h"

#pragma pack(push, 1)
typedef struct TraceHeaderCore {
    uint8_t magic[8];
    uint32_t schema_version;
    uint32_t header_size;
    uint32_t record_size;
    uint32_t manifest_entry_size;
    uint8_t target_sha256[32];
    uint8_t config_sha256[32];
    uint8_t catalogue_sha256[32];
    uint8_t manifest_sha256[32];
    uint32_t preferred_base;
    uint32_t image_base;
    uint32_t image_size;
    uint32_t process_id;
    uint64_t started_filetime_utc;
    uint64_t qpc_frequency;
    uint64_t started_qpc;
    uint8_t capture_id[16];
    uint32_t hook_count;
    uint32_t enabled_hook_count;
    uint32_t profile_id;
    uint32_t flags;
    uint64_t max_data_records;
    uint32_t queue_capacity;
    uint32_t region_count;
    uint32_t region_bytes;
    uint32_t read_call_budget;
    uint32_t checkpoint_interval;
    uint32_t flush_interval_ms;
    uint32_t activation_state;
    char tracer_version[32];
    char target_name[32];
    char profile_name[32];
    char config_name[64];
    uint8_t header_core_sha256[32];
    uint8_t reserved[572];
} TraceHeaderCore;

typedef struct TraceManifestRegion {
    uint32_t source;
    int32_t offset;
    uint32_t length;
    uint32_t phase_mask;
    char label[16];
} TraceManifestRegion;

typedef struct TraceManifestEntry {
    uint32_t id;
    uint32_t address;
    uint32_t rva;
    uint32_t profile_mask;
    uint32_t enabled;
    uint32_t divisor;
    uint32_t arg_count;
    int32_t this_adjust;
    uint32_t flags;
    uint32_t return_kind;
    uint32_t arg_type_bits;
    uint32_t relocation_kind;
    uint32_t relocation_offset;
    uint32_t relocation_target;
    uint32_t stolen_len;
    uint32_t instruction_count;
    uint8_t stolen[16];
    uint8_t instruction_lengths[8];
    char name[72];
    char config_key[48];
    char recovery[16];
    TraceManifestRegion regions[TRACE_REGION_COUNT];
} TraceManifestEntry;

typedef struct TraceFileHeader {
    TraceHeaderCore core;
    TraceManifestEntry manifest[TRACE_HOOK_COUNT];
    uint8_t padding[TRACE_HEADER_PADDING_SIZE];
} TraceFileHeader;

typedef struct TraceEvent {
    uint32_t magic;
    uint16_t size;
    uint8_t schema_version;
    uint8_t kind;
    uint64_t sequence;
    uint64_t qpc;
    uint64_t call_index;
    uint64_t sample_index;
    uint32_t tick_ms;
    uint32_t thread_id;
    uint32_t hook_id;
    uint32_t image_address;
    uint32_t raw_this;
    uint32_t normalized_this;
    uint32_t return_address;
    uint32_t eax;
    uint32_t edx;
    uint32_t eflags;
    uint32_t arg_valid_mask;
    uint32_t args[8];
    uint32_t region_address[TRACE_REGION_COUNT];
    uint32_t region_valid_mask[TRACE_REGION_COUNT];
    uint16_t region_requested[TRACE_REGION_COUNT];
    uint16_t region_captured[TRACE_REGION_COUNT];
    uint32_t event_flags;
    uint32_t read_calls_used;
    uint64_t admitted_records;
    uint64_t enqueued_records;
    uint64_t queue_full_losses;
    uint64_t budget_rejections;
    uint64_t read_faults;
    uint64_t read_budget_exceeded;
    uint64_t write_failures;
    uint64_t written_records;
    uint32_t queue_high_water;
    uint32_t terminal_reason;
    uint8_t record_sha256_128[16];
    uint8_t reserved[8];
    uint8_t regions[TRACE_REGION_COUNT][TRACE_REGION_BYTES];
} TraceEvent;

typedef struct CpuSnapshot {
    uint32_t edi;
    uint32_t esi;
    uint32_t ebp;
    uint32_t saved_esp;
    uint32_t ebx;
    uint32_t edx;
    uint32_t ecx;
    uint32_t eax;
    uint32_t eflags;
} CpuSnapshot;
#pragma pack(pop)

typedef char TraceGeneratedHookCountCheck[(TRACE_GENERATED_HOOK_COUNT == TRACE_HOOK_COUNT) ? 1 : -1];
typedef char TraceHeaderCoreSizeCheck[(sizeof(TraceHeaderCore) == TRACE_HEADER_CORE_SIZE) ? 1 : -1];
typedef char TraceManifestSizeCheck[(sizeof(TraceManifestEntry) == TRACE_MANIFEST_ENTRY_SIZE) ? 1 : -1];
typedef char TraceHeaderSizeCheck[(sizeof(TraceFileHeader) == TRACE_HEADER_SIZE) ? 1 : -1];
typedef char TraceEventMetaSizeCheck[((sizeof(TraceEvent) - TRACE_REGION_COUNT * TRACE_REGION_BYTES) == TRACE_RECORD_META_SIZE) ? 1 : -1];
typedef char TraceEventSizeCheck[(sizeof(TraceEvent) == TRACE_RECORD_SIZE) ? 1 : -1];
typedef char TraceSnapshotSizeCheck[(sizeof(CpuSnapshot) == 36u) ? 1 : -1];

typedef struct TraceHook {
    const TraceHookDefinition *definition;
    uint32_t divisor;
    uint8_t *target;
    uint8_t *stub;
    uint64_t original_word;
    uint64_t patch_word;
    DWORD original_protection;
    uint8_t installed;
    uint8_t ever_reachable;
} TraceHook;

typedef struct TraceReadContext {
    uint32_t calls;
    uint32_t budget_reported;
} TraceReadContext;

typedef struct SuspendedThread {
    HANDLE handle;
    DWORD id;
} SuspendedThread;

static void TRACE_CALL trace_enter(uint32_t hook_id, const CpuSnapshot *snapshot);
__declspec(dllexport) void __cdecl InitializeASI(void);

static HMODULE g_module;
static HANDLE g_capture = INVALID_HANDLE_VALUE;
static HANDLE g_wake_event;
static HANDLE g_startup_event;
static uint8_t *g_image_base;
static char g_module_dir[1024];
static char g_capture_dir[1024];
static char g_config_path[1024];
static char g_status_path[1024];
static char g_stop_request_path[1024];
static char g_capture_name[128];
static char g_capture_path[1024];
static uint8_t g_config_sha256[32];
static TraceFileHeader g_header;
static TraceHook g_hooks[TRACE_HOOK_COUNT];
static volatile LONG g_call_counts[TRACE_HOOK_COUNT];
static volatile LONG g_read_faults;
static volatile LONG g_read_budget_exceeded;
static volatile LONG g_accept_new;
static volatile LONG g_capture_active;
static volatile LONG g_stop;
static volatile LONG g_stop_reason;
static volatile LONG g_initialize_state;
static volatile LONG g_startup_signaled;
static CRITICAL_SECTION g_queue_lock;
static TraceEvent g_queue[TRACE_QUEUE_CAPACITY];
static uint32_t g_queue_head;
static uint32_t g_queue_tail;
static uint32_t g_queue_count;
static uint32_t g_queue_high_water;
static uint32_t g_inflight_builds;
static uint64_t g_next_sequence;
static uint64_t g_admitted_records;
static uint64_t g_enqueued_records;
static uint64_t g_queue_full_losses;
static uint64_t g_budget_rejections;
static uint64_t g_write_failures;
static uint64_t g_written_records;
static uint64_t g_written_data_records;
static uint32_t g_max_data_records = 100000u;
static uint32_t g_checkpoint_interval = 4096u;
static uint32_t g_flush_interval_ms = 1000u;
static uint32_t g_profile_id;
static uint32_t g_profile_mask;
static char g_profile_name[32];
static uint32_t g_zbware_probe_enabled;
static volatile LONG g_zbware_direct_probe_active;
static volatile LONG g_zbware_memory_probe_pending;
static volatile LONG g_zbware_memory_probe_observed;
static uint32_t g_zbware_memory_probe_started_tick;

static void mem_zero(void *destination, uint32_t size) {
    uint8_t *out = (uint8_t *)destination;
    uint32_t index;
    for (index = 0u; index < size; ++index) out[index] = 0u;
}

static void mem_copy(void *destination, const void *source, uint32_t size) {
    uint8_t *out = (uint8_t *)destination;
    const uint8_t *in = (const uint8_t *)source;
    uint32_t index;
    for (index = 0u; index < size; ++index) out[index] = in[index];
}

static int bytes_equal(const void *left, const void *right, uint32_t size) {
    const uint8_t *a = (const uint8_t *)left;
    const uint8_t *b = (const uint8_t *)right;
    uint32_t index;
    for (index = 0u; index < size; ++index) {
        if (a[index] != b[index]) return 0;
    }
    return 1;
}

static uint32_t text_len(const char *text) {
    uint32_t length = 0u;
    if (text == NULL) return 0u;
    while (text[length] != '\0') ++length;
    return length;
}

static int text_equal(const char *left, const char *right) {
    uint32_t index = 0u;
    if (left == NULL || right == NULL) return 0;
    while (left[index] != '\0' && right[index] != '\0') {
        if (left[index] != right[index]) return 0;
        ++index;
    }
    return left[index] == right[index];
}

static void copy_text(void *destination, uint32_t capacity, const char *source) {
    char *out = (char *)destination;
    uint32_t index = 0u;
    if (capacity == 0u) return;
    mem_zero(out, capacity);
    if (source == NULL) return;
    while (source[index] != '\0' && index + 1u < capacity) {
        out[index] = source[index];
        ++index;
    }
}

static int append_text(char *destination, uint32_t capacity, const char *text) {
    uint32_t at = text_len(destination);
    uint32_t index = 0u;
    if (at >= capacity) return 0;
    while (text != NULL && text[index] != '\0') {
        if (at + index + 1u >= capacity) return 0;
        destination[at + index] = text[index];
        ++index;
    }
    destination[at + index] = '\0';
    return 1;
}

static int append_uint_padded(char *destination, uint32_t capacity, uint32_t value, uint32_t width) {
    char digits[16];
    uint32_t count = 0u;
    uint32_t at = text_len(destination);
    uint32_t index;
    do {
        digits[count++] = (char)('0' + (value % 10u));
        value /= 10u;
    } while (value != 0u && count < sizeof(digits));
    while (count < width && count < sizeof(digits)) digits[count++] = '0';
    if (at + count + 1u > capacity) return 0;
    for (index = 0u; index < count; ++index) destination[at + index] = digits[count - index - 1u];
    destination[at + count] = '\0';
    return 1;
}

static void dirname_in_place(char *path) {
    uint32_t length = text_len(path);
    while (length > 0u) {
        --length;
        if (path[length] == '\\' || path[length] == '/') {
            path[length] = '\0';
            return;
        }
    }
    path[0] = '\0';
}

static int write_status(const char *level, const char *message, const char *detail) {
    HANDLE file;
    DWORD written = 0u;
    DWORD line_size;
    int succeeded;
    char line[1536];
    SYSTEMTIME now;
    mem_zero(line, sizeof(line));
    mem_zero(&now, sizeof(now));
    GetSystemTime(&now);
    if (!append_uint_padded(line, sizeof(line), now.wYear, 4u) ||
        !append_text(line, sizeof(line), "-") ||
        !append_uint_padded(line, sizeof(line), now.wMonth, 2u) ||
        !append_text(line, sizeof(line), "-") ||
        !append_uint_padded(line, sizeof(line), now.wDay, 2u) ||
        !append_text(line, sizeof(line), "T") ||
        !append_uint_padded(line, sizeof(line), now.wHour, 2u) ||
        !append_text(line, sizeof(line), ":") ||
        !append_uint_padded(line, sizeof(line), now.wMinute, 2u) ||
        !append_text(line, sizeof(line), ":") ||
        !append_uint_padded(line, sizeof(line), now.wSecond, 2u) ||
        !append_text(line, sizeof(line), ".") ||
        !append_uint_padded(line, sizeof(line), now.wMilliseconds, 3u) ||
        !append_text(line, sizeof(line), "Z ") ||
        !append_text(line, sizeof(line), level) ||
        !append_text(line, sizeof(line), " ") ||
        !append_text(line, sizeof(line), message)) return 0;
    if (detail != NULL && detail[0] != '\0') {
        if (!append_text(line, sizeof(line), " ") || !append_text(line, sizeof(line), detail)) return 0;
    }
    if (!append_text(line, sizeof(line), "\r\n")) return 0;
    line_size = text_len(line);
    file = CreateFileA(g_status_path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS,
                       FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return 0;
    succeeded = WriteFile(file, line, line_size, &written, NULL) && written == line_size && FlushFileBuffers(file);
    CloseHandle(file);
    return succeeded;
}

static int hash_file_sha256(const char *path, uint8_t output[32]) {
    HANDLE file;
    DWORD count = 0u;
    uint8_t buffer[32768];
    TraceSha256Context context;
    file = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING,
                       FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    if (file == INVALID_HANDLE_VALUE) return 0;
    trace_sha256_init(&context);
    for (;;) {
        if (!ReadFile(file, buffer, sizeof(buffer), &count, NULL)) {
            CloseHandle(file);
            return 0;
        }
        if (count == 0u) break;
        trace_sha256_update(&context, buffer, count);
    }
    CloseHandle(file);
    trace_sha256_finish(&context, output);
    return 1;
}

static int get_config_u32(const char *key, uint32_t default_value, uint32_t minimum, uint32_t maximum, uint32_t *output) {
    char buffer[64];
    uint32_t value;
    DWORD length;
    mem_zero(buffer, sizeof(buffer));
    length = GetPrivateProfileStringA("trace", key, "", buffer, sizeof(buffer), g_config_path);
    if (length == 0u) {
        *output = default_value;
        return 1;
    }
    if (length >= sizeof(buffer) - 1u) return 0;
    if (!trace_parse_u32(buffer, &value) || value < minimum || value > maximum) return 0;
    *output = value;
    return 1;
}

static int resolve_profile(void) {
    char requested[32];
    uint32_t index;
    mem_zero(requested, sizeof(requested));
    if (GetPrivateProfileStringA("trace", "profile", "survey", requested, sizeof(requested), g_config_path) == 0u) {
        copy_text(requested, sizeof(requested), "survey");
    }
    for (index = 0u; index < TRACE_GENERATED_PROFILE_COUNT; ++index) {
        if (text_equal(requested, kProfileDefinitions[index].name)) {
            g_profile_id = kProfileDefinitions[index].id;
            g_profile_mask = kProfileDefinitions[index].mask;
            copy_text(g_profile_name, sizeof(g_profile_name), kProfileDefinitions[index].name);
            return 1;
        }
    }
    write_status("ERROR", "unknown-profile", requested);
    return 0;
}

static int load_config(void) {
    uint32_t index;
    uint32_t enabled_count = 0u;
    uint32_t enabled = 0u;
    uint32_t schema = 0u;
    uint32_t capture_exits = 0u;
    if (!get_config_u32("enabled", 0u, 0u, 1u, &enabled) || enabled != 1u ||
        !get_config_u32("schema", TRACE_SCHEMA, TRACE_SCHEMA, TRACE_SCHEMA, &schema)) {
        write_status("ERROR", "invalid-enabled-or-schema-setting", g_config_path);
        return 0;
    }
    if (!resolve_profile()) return 0;
    if (!get_config_u32("zbware_probe", 0u, 0u, 1u, &g_zbware_probe_enabled) ||
        (g_zbware_probe_enabled != 0u && !text_equal(g_profile_name, "zbware"))) {
        write_status("ERROR", "invalid-zbware-probe-setting", "requires profile=zbware and value 0 or 1");
        return 0;
    }
    if (!get_config_u32("capture_exits", 0u, 0u, 1u, &capture_exits) || capture_exits != 0u) {
        write_status("ERROR", "schema4-is-entry-only-set-capture_exits-to-zero", g_config_path);
        return 0;
    }
    if (!get_config_u32("max_events", 100000u, 1000u, 1000000u, &g_max_data_records) ||
        !get_config_u32("checkpoint_interval", 4096u, 256u, 1000000u, &g_checkpoint_interval) ||
        !get_config_u32("flush_interval_ms", 1000u, 100u, 60000u, &g_flush_interval_ms)) {
        write_status("ERROR", "invalid-trace-limit-or-interval", g_config_path);
        return 0;
    }
    for (index = 0u; index < TRACE_HOOK_COUNT; ++index) {
        const TraceHookDefinition *definition = &kHookDefinitions[index];
        char buffer[64];
        DWORD length;
        uint32_t divisor = (definition->profile_mask & g_profile_mask) ? definition->default_divisor : 0u;
        mem_zero(buffer, sizeof(buffer));
        length = GetPrivateProfileStringA("trace", definition->config_key, "", buffer, sizeof(buffer), g_config_path);
        if (length >= sizeof(buffer) - 1u ||
            (length != 0u && (!trace_parse_u32(buffer, &divisor) || divisor > 1000000000u))) {
            write_status("ERROR", "invalid-hook-divisor", definition->config_key);
            return 0;
        }
        g_hooks[index].definition = definition;
        g_hooks[index].divisor = divisor;
        if (divisor != 0u) ++enabled_count;
    }
    if (enabled_count == 0u) {
        write_status("ERROR", "profile-resolved-no-hooks", g_profile_name);
        return 0;
    }
    return 1;
}

static void set_zbware_memory_tracing(uint32_t enabled) {
    volatile LONG *setting = (volatile LONG *)(uintptr_t)trace_rebase_preferred_address(
        ZBWARE_MEMORY_TRACING_ADDRESS,
        TRACE_CATALOGUE_IMAGE_BASE,
        (uint32_t)(uintptr_t)g_image_base
    );
    InterlockedExchange(setting, enabled != 0u ? 1 : 0);
}

static void run_zbware_direct_probes(void) {
    typedef void (__cdecl *ReleasePutStringFunction)(char, const char *);
    typedef DWORD (__cdecl *LaunchWindowsFunction)(char *);
    typedef int (__cdecl *HasTickerExpiredFunction)(uint32_t, float);
    char empty_command[1] = {'\0'};
    ReleasePutStringFunction release_put_string;
    LaunchWindowsFunction launch_windows;
    HasTickerExpiredFunction has_ticker_expired;
    uint32_t image_base = (uint32_t)(uintptr_t)g_image_base;

    release_put_string = (ReleasePutStringFunction)(uintptr_t)trace_rebase_preferred_address(
        ZBWARE_RELEASE_PUT_STRING_ADDRESS, TRACE_CATALOGUE_IMAGE_BASE, image_base);
    launch_windows = (LaunchWindowsFunction)(uintptr_t)trace_rebase_preferred_address(
        ZBWARE_LAUNCH_WINDOWS_ADDRESS, TRACE_CATALOGUE_IMAGE_BASE, image_base);
    has_ticker_expired = (HasTickerExpiredFunction)(uintptr_t)trace_rebase_preferred_address(
        ZBWARE_HAS_TICKER_EXPIRED_ADDRESS, TRACE_CATALOGUE_IMAGE_BASE, image_base);

    InterlockedExchange(&g_zbware_direct_probe_active, 1);
    write_status("INFO", "zbware-probe-call", "hook 49 bReleasePutString; output disabled");
    release_put_string(0, "NFSMWAITrace zBWare controlled probe\n");
    write_status("INFO", "zbware-probe-call", "hook 50 bLaunchWindows; empty command");
    (void)launch_windows(empty_command);
    write_status("INFO", "zbware-probe-call", "hook 54 bHasTickerExpired; zero duration");
    (void)has_ticker_expired(0u, 0.0f);
    InterlockedExchange(&g_zbware_direct_probe_active, 0);
    write_status("INFO", "zbware-direct-probes-complete", "hooks 49, 50, and 54; synthetic");

    set_zbware_memory_tracing(1u);
    g_zbware_memory_probe_started_tick = GetTickCount();
    InterlockedExchange(&g_zbware_memory_probe_pending, 1);
    write_status("INFO", "zbware-memory-probe-armed", "hook 55; resets after first call or timeout");
}

static int make_paths(void) {
    SYSTEMTIME now;
    DWORD length;
    DWORD attributes;
    DWORD process_id;
    mem_zero(g_module_dir, sizeof(g_module_dir));
    length = GetModuleFileNameA(g_module, g_module_dir, sizeof(g_module_dir));
    if (length == 0u || length >= sizeof(g_module_dir)) return 0;
    dirname_in_place(g_module_dir);
    if (g_module_dir[0] == '\0') return 0;
    mem_zero(g_capture_dir, sizeof(g_capture_dir));
    if (!append_text(g_capture_dir, sizeof(g_capture_dir), g_module_dir) ||
        !append_text(g_capture_dir, sizeof(g_capture_dir), "\\NFSMWAITrace")) return 0;
    if (!CreateDirectoryA(g_capture_dir, NULL)) {
        if (GetLastError() != ERROR_ALREADY_EXISTS) return 0;
        attributes = GetFileAttributesA(g_capture_dir);
        if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0u) return 0;
    }
    mem_zero(g_config_path, sizeof(g_config_path));
    if (!append_text(g_config_path, sizeof(g_config_path), g_module_dir) ||
        !append_text(g_config_path, sizeof(g_config_path), "\\NFSMWAITrace.ini")) return 0;
    mem_zero(g_status_path, sizeof(g_status_path));
    if (!append_text(g_status_path, sizeof(g_status_path), g_capture_dir) ||
        !append_text(g_status_path, sizeof(g_status_path), "\\status.log")) return 0;
    GetSystemTime(&now);
    process_id = GetCurrentProcessId();
    mem_zero(g_capture_name, sizeof(g_capture_name));
    if (!append_text(g_capture_name, sizeof(g_capture_name), "capture-") ||
        !append_uint_padded(g_capture_name, sizeof(g_capture_name), now.wYear, 4u) ||
        !append_uint_padded(g_capture_name, sizeof(g_capture_name), now.wMonth, 2u) ||
        !append_uint_padded(g_capture_name, sizeof(g_capture_name), now.wDay, 2u) ||
        !append_text(g_capture_name, sizeof(g_capture_name), "-") ||
        !append_uint_padded(g_capture_name, sizeof(g_capture_name), now.wHour, 2u) ||
        !append_uint_padded(g_capture_name, sizeof(g_capture_name), now.wMinute, 2u) ||
        !append_uint_padded(g_capture_name, sizeof(g_capture_name), now.wSecond, 2u) ||
        !append_text(g_capture_name, sizeof(g_capture_name), "-") ||
        !append_uint_padded(g_capture_name, sizeof(g_capture_name), process_id, 1u) ||
        !append_text(g_capture_name, sizeof(g_capture_name), ".bin")) return 0;
    mem_zero(g_capture_path, sizeof(g_capture_path));
    if (!append_text(g_capture_path, sizeof(g_capture_path), g_capture_dir) ||
        !append_text(g_capture_path, sizeof(g_capture_path), "\\") ||
        !append_text(g_capture_path, sizeof(g_capture_path), g_capture_name)) return 0;
    mem_zero(g_stop_request_path, sizeof(g_stop_request_path));
    return append_text(g_stop_request_path, sizeof(g_stop_request_path), g_capture_dir) &&
           append_text(g_stop_request_path, sizeof(g_stop_request_path), "\\") &&
           append_text(g_stop_request_path, sizeof(g_stop_request_path), g_capture_name) &&
           append_text(g_stop_request_path, sizeof(g_stop_request_path), ".stop.request");
}

static void cleanup_stop_request(void) {
    DWORD error;
    if (g_stop_request_path[0] == '\0' || DeleteFileA(g_stop_request_path)) return;
    error = GetLastError();
    if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND) {
        write_status("ERROR", "stop-request-cleanup-failed", g_capture_name);
    }
}

static int consume_stop_request(void) {
    DWORD attributes = GetFileAttributesA(g_stop_request_path);
    if (attributes == INVALID_FILE_ATTRIBUTES) return 0;
    if (!DeleteFileA(g_stop_request_path)) {
        write_status("ERROR", "stop-request-consume-failed", g_capture_name);
    }
    return 1;
}

static void signal_startup_complete(void) {
    if (InterlockedCompareExchange(&g_startup_signaled, 1, 0) != 0) return;
    InterlockedExchange(&g_initialize_state, 2);
    if (g_startup_event != NULL) SetEvent(g_startup_event);
}

static void mark_startup_failed(void) {
    if (g_capture_name[0] != '\0') {
        cleanup_stop_request();
        write_status("ERROR", "tracer-startup-failed", g_capture_name);
    }
    signal_startup_complete();
}

static DWORD fail_startup(void) {
    mark_startup_failed();
    return 0u;
}

static int verify_image(void) {
    IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)g_image_base;
    IMAGE_NT_HEADERS32 *nt;
    if (dos == NULL || dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    nt = (IMAGE_NT_HEADERS32 *)(g_image_base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386) return 0;
    if (nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) return 0;
    return nt->OptionalHeader.ImageBase == TRACE_CATALOGUE_IMAGE_BASE &&
           nt->OptionalHeader.SizeOfImage == TRACE_CATALOGUE_IMAGE_SIZE;
}

static int validate_hook_definition(const TraceHookDefinition *definition) {
    uint32_t index;
    uint32_t total = 0u;
    uint32_t relative_count = 0u;
    int relocation_on_boundary = 0;
    if (definition->id == 0u || definition->id > TRACE_HOOK_COUNT ||
        definition->address != TRACE_CATALOGUE_IMAGE_BASE + definition->rva ||
        (definition->rva & (TRACE_ATOMIC_PATCH_BYTES - 1u)) != 0u ||
        definition->rva > TRACE_CATALOGUE_IMAGE_SIZE - TRACE_ATOMIC_PATCH_BYTES ||
        definition->stolen_len < 5u || definition->stolen_len > TRACE_MAX_STOLEN_BYTES ||
        definition->instruction_count == 0u || definition->instruction_count > TRACE_MAX_INSTRUCTIONS ||
        definition->arg_count > 8u) return 0;
    for (index = 0u; index < definition->instruction_count; ++index) {
        uint32_t length = definition->instruction_lengths[index];
        uint8_t opcode;
        int relative;
        if (length == 0u || length > 15u || total + length > definition->stolen_len) return 0;
        opcode = definition->stolen[total];
        relative = opcode == 0xe8u || opcode == 0xe9u || opcode == 0xebu ||
                   (opcode >= 0x70u && opcode <= 0x7fu) ||
                   (opcode >= 0xe0u && opcode <= 0xe3u) ||
                   (opcode == 0x0fu && length >= 2u &&
                    definition->stolen[total + 1u] >= 0x80u && definition->stolen[total + 1u] <= 0x8fu);
        if (total == definition->relocation_offset) relocation_on_boundary = 1;
        if (relative) {
            if (definition->relocation_kind != TRACE_RELOCATION_CALL_REL32 ||
                total != definition->relocation_offset || opcode != 0xe8u || length != 5u) return 0;
            ++relative_count;
        }
        total += length;
    }
    if (total != definition->stolen_len) return 0;
    if (definition->relocation_kind == TRACE_RELOCATION_NONE) {
        return relative_count == 0u && definition->relocation_offset == 0u && definition->relocation_target == 0u;
    }
    if (definition->relocation_kind != TRACE_RELOCATION_CALL_REL32 ||
        relative_count != 1u || !relocation_on_boundary ||
        definition->relocation_offset + 5u > definition->stolen_len ||
        definition->stolen[definition->relocation_offset] != 0xe8u ||
        trace_stub_relative_destination(
            definition->stolen + definition->relocation_offset,
            definition->address + definition->relocation_offset
        ) != definition->relocation_target ||
        definition->relocation_target < TRACE_CATALOGUE_IMAGE_BASE ||
        definition->relocation_target >= TRACE_CATALOGUE_IMAGE_BASE + TRACE_CATALOGUE_IMAGE_SIZE) return 0;
    return 1;
}

static int validate_all_anchors(void) {
    uint32_t index;
    for (index = 0u; index < TRACE_HOOK_COUNT; ++index) {
        const TraceHookDefinition *definition = &kHookDefinitions[index];
        TraceHook *hook = &g_hooks[index];
        if (!validate_hook_definition(definition) || definition->id != index + 1u) {
            write_status("ERROR", "invalid-generated-hook-definition", definition->name);
            return 0;
        }
        hook->target = g_image_base + definition->rva;
        if (!bytes_equal(hook->target, definition->stolen, definition->stolen_len)) {
            write_status("ERROR", "anchor-mismatch", definition->name);
            return 0;
        }
        mem_copy(&hook->original_word, hook->target, TRACE_ATOMIC_PATCH_BYTES);
    }
    return 1;
}

static int make_entry_stub(TraceHook *hook) {
    const TraceHookDefinition *definition = hook->definition;
    TraceStubSpec spec;
    uint8_t *code;
    uint32_t code_size;
    DWORD old_protection = 0u;
    code = (uint8_t *)VirtualAlloc(NULL, TRACE_STUB_CAPACITY, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (code == NULL) return 0;
    mem_zero(&spec, sizeof(spec));
    spec.stub_address = (uint32_t)(uintptr_t)code;
    spec.capture_active_address = (uint32_t)(uintptr_t)&g_capture_active;
    spec.trace_enter_address = (uint32_t)(uintptr_t)&trace_enter;
    spec.hook_id = definition->id;
    spec.target_address = (uint32_t)(uintptr_t)hook->target;
    mem_copy(spec.stolen, definition->stolen, sizeof(spec.stolen));
    spec.stolen_len = definition->stolen_len;
    spec.relocation_kind = definition->relocation_kind == TRACE_RELOCATION_CALL_REL32
                               ? TRACE_STUB_RELOCATION_CALL_REL32
                               : TRACE_STUB_RELOCATION_NONE;
    spec.relocation_offset = definition->relocation_offset;
    spec.relocation_target = definition->relocation_kind == TRACE_RELOCATION_CALL_REL32
                                 ? trace_rebase_preferred_address(
                                       definition->relocation_target,
                                       TRACE_CATALOGUE_IMAGE_BASE,
                                       (uint32_t)(uintptr_t)g_image_base)
                                 : 0u;
    code_size = trace_build_entry_stub(code, &spec);
    if (code_size == 0u) {
        VirtualFree(code, 0u, MEM_RELEASE);
        return 0;
    }
    if (!VirtualProtect(code, TRACE_STUB_CAPACITY, PAGE_EXECUTE_READ, &old_protection)) {
        VirtualFree(code, 0u, MEM_RELEASE);
        return 0;
    }
    if (!FlushInstructionCache(GetCurrentProcess(), code, code_size)) {
        VirtualFree(code, 0u, MEM_RELEASE);
        return 0;
    }
    hook->stub = code;
    hook->patch_word = trace_build_atomic_jump_word(
        hook->original_word,
        (uint32_t)(uintptr_t)hook->target,
        (uint32_t)(uintptr_t)hook->stub
    );
    return 1;
}

static void release_uninstalled_stubs(void) {
    uint32_t index;
    for (index = 0u; index < TRACE_HOOK_COUNT; ++index) {
        if (!g_hooks[index].installed && !g_hooks[index].ever_reachable && g_hooks[index].stub != NULL) {
            VirtualFree(g_hooks[index].stub, 0u, MEM_RELEASE);
            g_hooks[index].stub = NULL;
        }
    }
}

static uint64_t atomic_replace_hook_word(TraceHook *hook, uint64_t replacement, uint64_t expected) {
    return (uint64_t)InterlockedCompareExchange64(
        (volatile LONG64 *)(void *)hook->target,
        (LONG64)replacement,
        (LONG64)expected
    );
}

static int patch_hook_uncontended(TraceHook *hook) {
    DWORD old_protection = 0u;
    DWORD ignored = 0u;
    uint64_t observed;
    int flushed;
    int protected;
    if (((uintptr_t)hook->target & (TRACE_ATOMIC_PATCH_BYTES - 1u)) != 0u) return 0;
    if (!VirtualProtect(hook->target, TRACE_ATOMIC_PATCH_BYTES, PAGE_EXECUTE_READWRITE, &old_protection)) return 0;
    hook->original_protection = old_protection;
    observed = atomic_replace_hook_word(hook, hook->patch_word, hook->original_word);
    if (observed == hook->original_word) {
        /* One aligned interlocked exchange makes the complete jump reachable.
           Retain this stub for the process lifetime even if rollback succeeds. */
        hook->installed = 1u;
        hook->ever_reachable = 1u;
    }
    flushed = observed == hook->original_word &&
              FlushInstructionCache(GetCurrentProcess(), hook->target, TRACE_ATOMIC_PATCH_BYTES) != 0;
    protected = VirtualProtect(hook->target, TRACE_ATOMIC_PATCH_BYTES, old_protection, &ignored) != 0;
    return observed == hook->original_word && flushed && protected;
}

static int restore_hook_uncontended(TraceHook *hook) {
    DWORD old_protection = 0u;
    DWORD ignored = 0u;
    uint64_t observed = hook->original_word;
    int valid = 1;
    int flushed = 1;
    int protected;
    if (!hook->installed && hook->original_protection == 0u) return 1;
    if (!VirtualProtect(hook->target, TRACE_ATOMIC_PATCH_BYTES, PAGE_EXECUTE_READWRITE, &old_protection)) return 0;
    if (hook->installed) {
        observed = atomic_replace_hook_word(hook, hook->original_word, hook->patch_word);
        valid = observed == hook->patch_word || observed == hook->original_word;
        if (valid) {
            hook->installed = 0u;
            flushed = FlushInstructionCache(GetCurrentProcess(), hook->target, TRACE_ATOMIC_PATCH_BYTES) != 0;
        }
    }
    protected = VirtualProtect(
        hook->target,
        TRACE_ATOMIC_PATCH_BYTES,
        hook->original_protection != 0u ? hook->original_protection : old_protection,
        &ignored
    ) != 0;
    return valid && flushed && protected;
}

static int suspended_contains(const SuspendedThread *threads, uint32_t count, DWORD id) {
    uint32_t index;
    for (index = 0u; index < count; ++index) {
        if (threads[index].id == id) return 1;
    }
    return 0;
}

static int try_resume_one_thread(SuspendedThread *thread) {
    if (ResumeThread(thread->handle) != (DWORD)-1 ||
        /* A terminated thread handle is signaled; unlike
           GetExitCodeThread, this remains correct when the application exit
           code is STILL_ACTIVE (259). */
        WaitForSingleObject(thread->handle, 0u) == WAIT_OBJECT_0) {
        CloseHandle(thread->handle);
        thread->handle = NULL;
        thread->id = 0u;
        return 1;
    }
    return 0;
}

static void resume_threads(SuspendedThread *threads, uint32_t count) {
    for (;;) {
        uint32_t index = count;
        uint32_t remaining = 0u;
        while (index > 0u) {
            --index;
            if (threads[index].handle != NULL && !try_resume_one_thread(&threads[index])) ++remaining;
        }
        if (remaining == 0u) return;
        /* Do not abandon any tracer-owned suspend count. Retain every failed
           handle, while continuing to release the other threads, and retry. */
        Sleep(1u);
    }
}

static void close_suspended_thread_handles(SuspendedThread *threads, uint32_t count) {
    uint32_t index;
    for (index = 0u; index < count; ++index) {
        if (threads[index].handle != NULL) {
            CloseHandle(threads[index].handle);
            threads[index].handle = NULL;
            threads[index].id = 0u;
        }
    }
}

static int suspended_threads_clear_of_hooks(const SuspendedThread *threads, uint32_t count) {
    uint32_t thread_index;
    for (thread_index = 0u; thread_index < count; ++thread_index) {
        CONTEXT context;
        uint32_t hook_index;
        mem_zero(&context, sizeof(context));
        context.ContextFlags = CONTEXT_CONTROL;
        if (!GetThreadContext(threads[thread_index].handle, &context)) return 0;
        for (hook_index = 0u; hook_index < TRACE_HOOK_COUNT; ++hook_index) {
            const TraceHook *hook = &g_hooks[hook_index];
            uint32_t begin;
            uint32_t end;
            if (hook->divisor == 0u) continue;
            begin = (uint32_t)(uintptr_t)hook->target;
            end = begin + hook->definition->stolen_len;
            if ((uint32_t)context.Eip >= begin && (uint32_t)context.Eip < end) return 0;
        }
    }
    return 1;
}

static int suspend_other_threads(SuspendedThread *threads, uint32_t *count) {
    DWORD process_id = GetCurrentProcessId();
    DWORD current_thread = GetCurrentThreadId();
    uint32_t round;
    uint32_t stable_scans = 0u;
    *count = 0u;
    for (round = 0u; round < TRACE_THREAD_SCAN_ROUNDS; ++round) {
        HANDLE snapshot;
        THREADENTRY32 entry;
        uint32_t added = 0u;
        snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0u);
        if (snapshot == INVALID_HANDLE_VALUE) return 0;
        mem_zero(&entry, sizeof(entry));
        entry.dwSize = sizeof(entry);
        if (!Thread32First(snapshot, &entry)) {
            CloseHandle(snapshot);
            return 0;
        }
        for (;;) {
            HANDLE thread;
            DWORD enumeration_error;
            if (entry.th32OwnerProcessID == process_id && entry.th32ThreadID != current_thread &&
                !suspended_contains(threads, *count, entry.th32ThreadID)) {
                if (*count >= TRACE_MAX_SUSPENDED_THREADS) {
                    CloseHandle(snapshot);
                    return 0;
                }
                thread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION | SYNCHRONIZE,
                                    FALSE, entry.th32ThreadID);
                if (thread == NULL || SuspendThread(thread) == (DWORD)-1) {
                    if (thread != NULL) CloseHandle(thread);
                    CloseHandle(snapshot);
                    return 0;
                }
                threads[*count].handle = thread;
                threads[*count].id = entry.th32ThreadID;
                ++*count;
                ++added;
            }
            SetLastError(ERROR_SUCCESS);
            if (Thread32Next(snapshot, &entry)) continue;
            enumeration_error = GetLastError();
            if (enumeration_error != ERROR_NO_MORE_FILES) {
                CloseHandle(snapshot);
                return 0;
            }
            break;
        }
        CloseHandle(snapshot);
        if (added == 0u) {
            ++stable_scans;
            if (stable_scans >= TRACE_THREAD_STABLE_SCANS) return 1;
        } else {
            stable_scans = 0u;
        }
        Sleep(1u);
    }
    return 0;
}

static int install_hooks_transaction(void) {
    SuspendedThread suspended[TRACE_MAX_SUSPENDED_THREADS];
    uint32_t suspended_count = 0u;
    uint32_t quiescence_attempt;
    uint32_t index;
    const char *failure_message = NULL;
    const char *failure_detail = NULL;
    int rollback_degraded = 0;
    mem_zero(suspended, sizeof(suspended));
    for (index = 0u; index < TRACE_HOOK_COUNT; ++index) {
        if (g_hooks[index].divisor != 0u && !make_entry_stub(&g_hooks[index])) {
            write_status("ERROR", "entry-stub-allocation-failed", g_hooks[index].definition->name);
            release_uninstalled_stubs();
            return 0;
        }
    }
    for (quiescence_attempt = 0u; quiescence_attempt < 64u; ++quiescence_attempt) {
        if (!suspend_other_threads(suspended, &suspended_count)) {
            resume_threads(suspended, suspended_count);
            close_suspended_thread_handles(suspended, suspended_count);
            release_uninstalled_stubs();
            write_status("ERROR", "thread-quiescence-failed", "no hooks patched");
            return 0;
        }
        if (suspended_threads_clear_of_hooks(suspended, suspended_count)) break;
        resume_threads(suspended, suspended_count);
        suspended_count = 0u;
        Sleep(1u);
    }
    if (quiescence_attempt == 64u) {
        release_uninstalled_stubs();
        write_status("ERROR", "thread-in-patch-span-timeout", "no hooks patched");
        return 0;
    }
    for (index = 0u; index < TRACE_HOOK_COUNT; ++index) {
        TraceHook *hook = &g_hooks[index];
        if (hook->divisor != 0u && !bytes_equal(hook->target, hook->definition->stolen, hook->definition->stolen_len)) {
            failure_message = "anchor-changed-during-patch";
            failure_detail = hook->definition->name;
            goto rollback;
        }
    }
    for (index = 0u; index < TRACE_HOOK_COUNT; ++index) {
        TraceHook *hook = &g_hooks[index];
        if (hook->divisor == 0u) continue;
        if (!patch_hook_uncontended(hook)) {
            failure_message = "hook-patch-failed";
            failure_detail = hook->definition->name;
            goto rollback;
        }
    }
    if (!FlushInstructionCache(GetCurrentProcess(), NULL, 0u)) {
        failure_message = "process-instruction-cache-flush-failed";
        failure_detail = "hooks rolled back before the startup gate opened";
        goto rollback;
    }
    resume_threads(suspended, suspended_count);
    return 1;

rollback:
    index = TRACE_HOOK_COUNT;
    while (index > 0u) {
        --index;
        if (!restore_hook_uncontended(&g_hooks[index])) rollback_degraded = 1;
    }
    resume_threads(suspended, suspended_count);
    close_suspended_thread_handles(suspended, suspended_count);
    release_uninstalled_stubs();
    write_status(
        "ERROR",
        rollback_degraded ? "hook-patch-rollback-degraded" :
        (failure_message != NULL ? failure_message : "hook-patch-rollback"),
        rollback_degraded ? "one or more inactive fallback hooks retained" :
        (failure_detail != NULL ? failure_detail : "no hooks active")
    );
    return 0;
}

static void finalize_header_core_sha256(void) {
    TraceSha256Context context;
    mem_zero(g_header.core.header_core_sha256, sizeof(g_header.core.header_core_sha256));
    trace_sha256_init(&context);
    trace_sha256_update(&context, (const uint8_t *)&g_header.core, sizeof(g_header.core));
    trace_sha256_finish(&context, g_header.core.header_core_sha256);
}

static void build_header(const uint8_t target_sha256[32]) {
    FILETIME filetime;
    LARGE_INTEGER qpc_frequency;
    LARGE_INTEGER started_qpc;
    TraceSha256Context context;
    uint8_t capture_digest[32];
    uint32_t index;
    uint32_t region_index;
    uint32_t enabled_count = 0u;
    mem_zero(&g_header, sizeof(g_header));
    g_header.core.magic[0] = 'N'; g_header.core.magic[1] = 'M'; g_header.core.magic[2] = 'W'; g_header.core.magic[3] = 'A';
    g_header.core.magic[4] = 'I'; g_header.core.magic[5] = 'T'; g_header.core.magic[6] = 'R'; g_header.core.magic[7] = '4';
    g_header.core.schema_version = TRACE_SCHEMA;
    g_header.core.header_size = TRACE_HEADER_SIZE;
    g_header.core.record_size = TRACE_RECORD_SIZE;
    g_header.core.manifest_entry_size = TRACE_MANIFEST_ENTRY_SIZE;
    mem_copy(g_header.core.target_sha256, target_sha256, 32u);
    mem_copy(g_header.core.config_sha256, g_config_sha256, 32u);
    mem_copy(g_header.core.catalogue_sha256, kCatalogueSha256, 32u);
    g_header.core.preferred_base = TRACE_CATALOGUE_IMAGE_BASE;
    g_header.core.image_base = (uint32_t)(uintptr_t)g_image_base;
    g_header.core.image_size = TRACE_CATALOGUE_IMAGE_SIZE;
    g_header.core.process_id = GetCurrentProcessId();
    GetSystemTimeAsFileTime(&filetime);
    g_header.core.started_filetime_utc = ((uint64_t)filetime.dwHighDateTime << 32u) | filetime.dwLowDateTime;
    mem_zero(&qpc_frequency, sizeof(qpc_frequency));
    mem_zero(&started_qpc, sizeof(started_qpc));
    (void)QueryPerformanceFrequency(&qpc_frequency);
    (void)QueryPerformanceCounter(&started_qpc);
    g_header.core.qpc_frequency = (uint64_t)qpc_frequency.QuadPart;
    g_header.core.started_qpc = (uint64_t)started_qpc.QuadPart;
    g_header.core.hook_count = TRACE_HOOK_COUNT;
    g_header.core.profile_id = g_profile_id;
    g_header.core.flags = TRACE_HEADER_ENTRY_ONLY | TRACE_HEADER_FXSAVE | TRACE_HEADER_PROFILE_PRESET | TRACE_HEADER_ORDERED_FIFO;
    g_header.core.max_data_records = g_max_data_records;
    g_header.core.queue_capacity = TRACE_QUEUE_CAPACITY;
    g_header.core.region_count = TRACE_REGION_COUNT;
    g_header.core.region_bytes = TRACE_REGION_BYTES;
    g_header.core.read_call_budget = TRACE_MAX_READ_CALLS;
    g_header.core.checkpoint_interval = g_checkpoint_interval;
    g_header.core.flush_interval_ms = g_flush_interval_ms;
    g_header.core.activation_state = TRACE_STATE_VALIDATED;
    copy_text(g_header.core.tracer_version, sizeof(g_header.core.tracer_version), kTraceVersionIdentity);
    copy_text(g_header.core.target_name, sizeof(g_header.core.target_name), TRACE_CATALOGUE_TARGET_NAME);
    copy_text(g_header.core.profile_name, sizeof(g_header.core.profile_name), g_profile_name);
    copy_text(g_header.core.config_name, sizeof(g_header.core.config_name), "NFSMWAITrace.ini");
    for (index = 0u; index < TRACE_HOOK_COUNT; ++index) {
        const TraceHookDefinition *definition = g_hooks[index].definition;
        TraceManifestEntry *entry = &g_header.manifest[index];
        entry->id = definition->id;
        entry->address = definition->address;
        entry->rva = definition->rva;
        entry->profile_mask = definition->profile_mask;
        entry->enabled = g_hooks[index].divisor != 0u;
        entry->divisor = g_hooks[index].divisor;
        entry->arg_count = definition->arg_count;
        entry->this_adjust = definition->this_adjust;
        entry->flags = definition->flags;
        entry->return_kind = definition->return_kind;
        entry->arg_type_bits = definition->arg_type_bits;
        entry->relocation_kind = definition->relocation_kind;
        entry->relocation_offset = definition->relocation_offset;
        entry->relocation_target = definition->relocation_target;
        entry->stolen_len = definition->stolen_len;
        entry->instruction_count = definition->instruction_count;
        mem_copy(entry->stolen, definition->stolen, sizeof(entry->stolen));
        mem_copy(entry->instruction_lengths, definition->instruction_lengths, sizeof(entry->instruction_lengths));
        copy_text(entry->name, sizeof(entry->name), definition->name);
        copy_text(entry->config_key, sizeof(entry->config_key), definition->config_key);
        copy_text(entry->recovery, sizeof(entry->recovery), definition->recovery);
        for (region_index = 0u; region_index < TRACE_REGION_COUNT; ++region_index) {
            entry->regions[region_index].source = definition->regions[region_index].source;
            entry->regions[region_index].offset = definition->regions[region_index].offset;
            entry->regions[region_index].length = definition->regions[region_index].length;
            entry->regions[region_index].phase_mask = definition->regions[region_index].length != 0u ? 1u : 0u;
            copy_text(entry->regions[region_index].label, sizeof(entry->regions[region_index].label),
                      definition->regions[region_index].label);
        }
        if (entry->enabled) ++enabled_count;
    }
    g_header.core.enabled_hook_count = enabled_count;
    trace_sha256_init(&context);
    trace_sha256_update(&context, (const uint8_t *)g_header.manifest,
                        TRACE_HEADER_SIZE - TRACE_HEADER_CORE_SIZE);
    trace_sha256_finish(&context, g_header.core.manifest_sha256);
    trace_sha256_init(&context);
    trace_sha256_update(&context, (const uint8_t *)&g_header.core.started_filetime_utc,
                        sizeof(g_header.core.started_filetime_utc));
    trace_sha256_update(&context, (const uint8_t *)&g_header.core.started_qpc, sizeof(g_header.core.started_qpc));
    trace_sha256_update(&context, (const uint8_t *)&g_header.core.process_id, sizeof(g_header.core.process_id));
    trace_sha256_update(&context, g_header.core.config_sha256, sizeof(g_header.core.config_sha256));
    trace_sha256_finish(&context, capture_digest);
    mem_copy(g_header.core.capture_id, capture_digest, sizeof(g_header.core.capture_id));
    finalize_header_core_sha256();
}

static int open_capture(void) {
    DWORD written = 0u;
    g_capture = CreateFileA(g_capture_path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_NEW,
                            FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    if (g_capture == INVALID_HANDLE_VALUE) return 0;
    if (!WriteFile(g_capture, &g_header, sizeof(g_header), &written, NULL) || written != sizeof(g_header) ||
        !FlushFileBuffers(g_capture)) {
        CloseHandle(g_capture);
        g_capture = INVALID_HANDLE_VALUE;
        return 0;
    }
    return 1;
}

static int update_header_state(uint32_t state, int flush) {
    LARGE_INTEGER zero;
    LARGE_INTEGER current;
    DWORD written = 0u;
    if (g_capture == INVALID_HANDLE_VALUE) return 0;
    zero.QuadPart = 0;
    current.QuadPart = TRACE_HEADER_SIZE;
    g_header.core.activation_state = state;
    finalize_header_core_sha256();
    if (!SetFilePointerEx(g_capture, zero, &current, FILE_CURRENT) ||
        !SetFilePointerEx(g_capture, zero, NULL, FILE_BEGIN) ||
        !WriteFile(g_capture, &g_header.core, sizeof(g_header.core), &written, NULL) ||
        written != sizeof(g_header.core) ||
        !SetFilePointerEx(g_capture, current, NULL, FILE_BEGIN)) {
        (void)SetFilePointerEx(g_capture, current, NULL, FILE_BEGIN);
        return 0;
    }
    return !flush || FlushFileBuffers(g_capture);
}

static void record_read_fault(void) {
    (void)InterlockedIncrement(&g_read_faults);
}

static void record_read_budget(TraceReadContext *context) {
    if (!context->budget_reported) {
        context->budget_reported = 1u;
        (void)InterlockedIncrement(&g_read_budget_exceeded);
    }
}

static int bounded_read(TraceReadContext *context, const void *source, void *destination, uint32_t size, uint32_t *read_size) {
    SIZE_T count = 0u;
    BOOL succeeded;
    if (read_size != NULL) *read_size = 0u;
    if (source == NULL || destination == NULL || size == 0u) return 0;
    if (context->calls >= TRACE_MAX_READ_CALLS) {
        record_read_budget(context);
        return 0;
    }
    ++context->calls;
    succeeded = ReadProcessMemory(GetCurrentProcess(), source, destination, size, &count);
    if (!succeeded || count != size) {
        record_read_fault();
        if (read_size != NULL) *read_size = (uint32_t)count;
        return 0;
    }
    if (read_size != NULL) *read_size = (uint32_t)count;
    return 1;
}

static int bounded_query(TraceReadContext *context, const void *address, MEMORY_BASIC_INFORMATION *information) {
    if (context->calls >= TRACE_MAX_READ_CALLS) {
        record_read_budget(context);
        return 0;
    }
    ++context->calls;
    if (VirtualQuery(address, information, sizeof(*information)) != sizeof(*information)) {
        record_read_fault();
        return 0;
    }
    return 1;
}

static int memory_region_readable(const MEMORY_BASIC_INFORMATION *information) {
    DWORD protection;
    if (information->State != MEM_COMMIT) return 0;
    protection = information->Protect & 0xffu;
    if ((information->Protect & PAGE_GUARD) != 0u || protection == PAGE_NOACCESS || protection == 0u) return 0;
    return 1;
}

static uint32_t validity_mask(const uint8_t valid[TRACE_REGION_BYTES], uint32_t requested) {
    uint32_t block;
    uint32_t mask = 0u;
    uint32_t block_count = (requested + 15u) / 16u;
    for (block = 0u; block < block_count; ++block) {
        uint32_t begin = block * 16u;
        uint32_t end = begin + 16u;
        uint32_t index;
        int complete = 1;
        if (end > requested) end = requested;
        for (index = begin; index < end; ++index) {
            if (!valid[index]) {
                complete = 0;
                break;
            }
        }
        if (complete) mask |= 1u << block;
    }
    return mask;
}

static void capture_region(TraceReadContext *context, uint32_t address, uint32_t requested,
                           uint8_t output[TRACE_REGION_BYTES], uint16_t *captured, uint32_t *valid_mask) {
    uint8_t valid[TRACE_REGION_BYTES];
    uint32_t count = 0u;
    uint64_t cursor;
    uint64_t end;
    mem_zero(output, TRACE_REGION_BYTES);
    mem_zero(valid, sizeof(valid));
    *captured = 0u;
    *valid_mask = 0u;
    if (address == 0u || requested > TRACE_REGION_BYTES ||
        !trace_checked_u32_span(address, requested, &end)) return;
    if (bounded_read(context, (const void *)(uintptr_t)address, output, requested, &count)) {
        mem_zero(valid, requested);
        for (cursor = 0u; cursor < requested; ++cursor) valid[cursor] = 1u;
        *captured = (uint16_t)requested;
        *valid_mask = validity_mask(valid, requested);
        return;
    }
    mem_zero(output, requested);
    cursor = address;
    while (cursor < end && context->calls < TRACE_MAX_READ_CALLS) {
        MEMORY_BASIC_INFORMATION information;
        uint32_t region_begin;
        uint64_t region_end;
        uint32_t span;
        uint32_t offset;
        mem_zero(&information, sizeof(information));
        if (!bounded_query(context, (const void *)(uintptr_t)(uint32_t)cursor, &information)) break;
        region_begin = (uint32_t)(uintptr_t)information.BaseAddress;
        region_end = (uint64_t)region_begin + (uint64_t)information.RegionSize;
        if (region_end > UINT64_C(0x100000000)) region_end = UINT64_C(0x100000000);
        if (region_end <= cursor) break;
        if (region_end > end) region_end = end;
        span = (uint32_t)(region_end - cursor);
        offset = (uint32_t)(cursor - address);
        if (memory_region_readable(&information) && span != 0u) {
            uint32_t read_size = 0u;
            if (bounded_read(context, (const void *)(uintptr_t)(uint32_t)cursor, output + offset, span, &read_size)) {
                uint32_t index;
                for (index = 0u; index < span; ++index) valid[offset + index] = 1u;
                *captured = (uint16_t)(*captured + span);
            }
        }
        cursor = region_end;
    }
    if (cursor < end && context->calls >= TRACE_MAX_READ_CALLS) record_read_budget(context);
    *valid_mask = validity_mask(valid, requested);
}

static int add_signed_offset(uint32_t base, int32_t offset, uint32_t *result) {
    int64_t value = (int64_t)(uint64_t)base + (int64_t)offset;
    if (value <= 0 || value > 0xffffffffll) return 0;
    *result = (uint32_t)value;
    return 1;
}

static int normalize_this(const TraceHookDefinition *definition, uint32_t raw_this, uint32_t *normalized) {
    if ((definition->flags & TRACE_HOOK_STATIC) != 0u || raw_this == 0u) return 0;
    return add_signed_offset(raw_this, definition->this_adjust, normalized);
}

static int resolve_region_address(const TraceHookDefinition *definition, const TraceEvent *event,
                                  const TraceRegionDefinition *region, uint32_t *address) {
    uint32_t base;
    if (region->source == TRACE_SOURCE_NONE || region->length == 0u) return 0;
    if (region->source == TRACE_SOURCE_NORMALIZED_THIS) {
        if ((event->event_flags & TRACE_EVENT_NORMALIZED_THIS_VALID) == 0u) return 0;
        base = event->normalized_this;
        return add_signed_offset(base, region->offset, address);
    }
    if (region->source == TRACE_SOURCE_RAW_THIS) {
        return add_signed_offset(event->raw_this, region->offset, address);
    }
    if (region->source >= TRACE_SOURCE_ARG0 && region->source <= TRACE_SOURCE_ARG7) {
        uint32_t argument_index = region->source - TRACE_SOURCE_ARG0;
        if (argument_index >= definition->arg_count || (event->arg_valid_mask & (1u << argument_index)) == 0u) return 0;
        return add_signed_offset(event->args[argument_index], region->offset, address);
    }
    if (region->source == TRACE_SOURCE_ABSOLUTE) {
        uint32_t preferred_address = (uint32_t)region->offset;
        if (preferred_address < TRACE_CATALOGUE_IMAGE_BASE ||
            preferred_address >= TRACE_CATALOGUE_IMAGE_BASE + TRACE_CATALOGUE_IMAGE_SIZE) return 0;
        *address = (uint32_t)(uintptr_t)g_image_base + (preferred_address - TRACE_CATALOGUE_IMAGE_BASE);
        return 1;
    }
    return 0;
}

static int reserve_data_record(void) {
    int accepted = 0;
    EnterCriticalSection(&g_queue_lock);
    if (InterlockedCompareExchange(&g_accept_new, 0, 0) != 0 && g_admitted_records < g_max_data_records) {
        ++g_admitted_records;
        ++g_inflight_builds;
        accepted = 1;
        if (g_admitted_records >= g_max_data_records) {
            InterlockedExchange(&g_accept_new, 0);
            InterlockedCompareExchange(&g_stop_reason, TRACE_TERMINAL_LIMIT, TRACE_TERMINAL_NONE);
        }
    } else if (InterlockedCompareExchange(&g_capture_active, 0, 0) != 0 &&
               InterlockedCompareExchange(&g_stop_reason, 0, 0) == TRACE_TERMINAL_LIMIT) {
        ++g_budget_rejections;
    }
    LeaveCriticalSection(&g_queue_lock);
    if (!accepted && g_wake_event != NULL) SetEvent(g_wake_event);
    return accepted;
}

static void snapshot_counters_locked(TraceEvent *event) {
    event->admitted_records = g_admitted_records;
    event->enqueued_records = g_enqueued_records;
    event->queue_full_losses = g_queue_full_losses;
    event->budget_rejections = g_budget_rejections;
    event->read_faults = (uint32_t)InterlockedCompareExchange(&g_read_faults, 0, 0);
    event->read_budget_exceeded = (uint32_t)InterlockedCompareExchange(&g_read_budget_exceeded, 0, 0);
    event->write_failures = g_write_failures;
    event->written_records = g_written_records;
    event->queue_high_water = g_queue_high_water;
}

static void finish_data_record(TraceEvent *event) {
    int queued = 0;
    EnterCriticalSection(&g_queue_lock);
    if (g_queue_count < TRACE_QUEUE_CAPACITY) {
        event->sequence = ++g_next_sequence;
        ++g_enqueued_records;
        if (g_queue_count + 1u > g_queue_high_water) g_queue_high_water = g_queue_count + 1u;
        snapshot_counters_locked(event);
        mem_copy(&g_queue[g_queue_tail], event, sizeof(*event));
        g_queue_tail = (g_queue_tail + 1u) % TRACE_QUEUE_CAPACITY;
        ++g_queue_count;
        queued = 1;
    } else {
        ++g_queue_full_losses;
    }
    if (g_inflight_builds != 0u) --g_inflight_builds;
    LeaveCriticalSection(&g_queue_lock);
    if ((queued || InterlockedCompareExchange(&g_accept_new, 0, 0) == 0) && g_wake_event != NULL) SetEvent(g_wake_event);
}

static void TRACE_CALL trace_enter(uint32_t hook_id, const CpuSnapshot *snapshot) {
    DWORD saved_last_error = GetLastError();
    const TraceHookDefinition *definition;
    TraceHook *hook;
    TraceEvent event;
    TraceReadContext read_context;
    uint32_t call_index;
    uint64_t sample_index;
    uint32_t stack_words[9];
    uint32_t stack_word_count;
    uint32_t stack_address;
    uint64_t stack_end;
    uint32_t read_size = 0u;
    uint32_t index;
    uint32_t probe_flags = 0u;
    LARGE_INTEGER qpc;
    if (hook_id == 0u || hook_id > TRACE_HOOK_COUNT || snapshot == NULL ||
        InterlockedCompareExchange(&g_capture_active, 0, 0) == 0) goto done;
    hook = &g_hooks[hook_id - 1u];
    definition = hook->definition;
    if (hook->divisor == 0u) goto done;
    if (InterlockedCompareExchange(&g_zbware_direct_probe_active, 0, 0) != 0) {
        probe_flags |= TRACE_EVENT_SYNTHETIC_PROBE;
    }
    if (hook_id == ZBWARE_TRACE_NEW_POOL_HOOK_ID &&
        InterlockedCompareExchange(&g_zbware_memory_probe_pending, 0, 1) == 1) {
        set_zbware_memory_tracing(0u);
        InterlockedExchange(&g_zbware_memory_probe_observed, 1);
        probe_flags |= TRACE_EVENT_PROBE_ASSISTED;
    }
    call_index = (uint32_t)InterlockedIncrement(&g_call_counts[hook_id - 1u]);
    if (call_index == 0u) {
        InterlockedExchange(&g_accept_new, 0);
        InterlockedCompareExchange(&g_stop_reason, TRACE_TERMINAL_CALL_INDEX_WRAP, TRACE_TERMINAL_NONE);
        if (g_wake_event != NULL) SetEvent(g_wake_event);
        goto done;
    }
    if (((call_index - 1u) % hook->divisor) != 0u) goto done;
    sample_index = ((call_index - 1u) / hook->divisor) + 1u;
    if (!reserve_data_record()) goto done;
    mem_zero(&event, sizeof(event));
    mem_zero(&read_context, sizeof(read_context));
    mem_zero(stack_words, sizeof(stack_words));
    event.magic = TRACE_EVENT_MAGIC;
    event.size = (uint16_t)sizeof(event);
    event.schema_version = TRACE_SCHEMA;
    event.kind = TRACE_EVENT_ENTRY;
    event.call_index = call_index;
    event.sample_index = sample_index;
    event.tick_ms = GetTickCount();
    event.thread_id = GetCurrentThreadId();
    event.hook_id = hook_id;
    event.event_flags = probe_flags;
    event.image_address = definition->address;
    event.raw_this = snapshot->ecx;
    event.eax = snapshot->eax;
    event.edx = snapshot->edx;
    event.eflags = snapshot->eflags;
    mem_zero(&qpc, sizeof(qpc));
    if (QueryPerformanceCounter(&qpc)) event.qpc = (uint64_t)qpc.QuadPart;
    if (normalize_this(definition, event.raw_this, &event.normalized_this)) {
        event.event_flags |= TRACE_EVENT_NORMALIZED_THIS_VALID;
    }
    stack_word_count = definition->arg_count + 1u;
    if (snapshot->saved_esp <= 0xffffffffu - 4u) {
        stack_address = snapshot->saved_esp + 4u;
        if (trace_checked_u32_span(stack_address, stack_word_count * sizeof(uint32_t), &stack_end) &&
            bounded_read(&read_context, (const void *)(uintptr_t)stack_address, stack_words,
                         stack_word_count * sizeof(uint32_t), &read_size)) {
            event.event_flags |= TRACE_EVENT_RETURN_ADDRESS_VALID;
            event.return_address = stack_words[0];
            event.arg_valid_mask = definition->arg_count == 0u ? 0u : ((1u << definition->arg_count) - 1u);
            for (index = 0u; index < definition->arg_count; ++index) event.args[index] = stack_words[index + 1u];
        } else if (trace_checked_u32_span(stack_address, stack_word_count * sizeof(uint32_t), &stack_end)) {
            for (index = 0u; index < stack_word_count && read_context.calls < TRACE_MAX_READ_CALLS; ++index) {
                uint32_t word = 0u;
                uint64_t word_address = (uint64_t)stack_address + (uint64_t)index * sizeof(uint32_t);
                if (word_address + sizeof(uint32_t) <= stack_end &&
                    bounded_read(&read_context, (const void *)(uintptr_t)(uint32_t)word_address,
                                 &word, sizeof(word), &read_size)) {
                    if (index == 0u) {
                        event.return_address = word;
                        event.event_flags |= TRACE_EVENT_RETURN_ADDRESS_VALID;
                    }
                    else {
                        event.args[index - 1u] = word;
                        event.arg_valid_mask |= 1u << (index - 1u);
                    }
                }
            }
        }
    }
    for (index = 0u; index < TRACE_REGION_COUNT; ++index) {
        const TraceRegionDefinition *region = &definition->regions[index];
        uint32_t address = 0u;
        event.region_requested[index] = (uint16_t)region->length;
        if (resolve_region_address(definition, &event, region, &address)) {
            event.region_address[index] = address;
            capture_region(&read_context, address, region->length, event.regions[index],
                           &event.region_captured[index], &event.region_valid_mask[index]);
            if (event.region_captured[index] != event.region_requested[index]) event.event_flags |= TRACE_EVENT_REGION_PARTIAL;
        } else if (region->length != 0u) {
            event.event_flags |= TRACE_EVENT_REGION_PARTIAL;
        }
    }
    event.read_calls_used = read_context.calls;
    if (read_context.budget_reported) event.event_flags |= TRACE_EVENT_READ_BUDGET_EXCEEDED;
    finish_data_record(&event);
done:
    SetLastError(saved_last_error);
}

static int pop_queue(TraceEvent *event) {
    int found = 0;
    EnterCriticalSection(&g_queue_lock);
    if (g_queue_count != 0u) {
        mem_copy(event, &g_queue[g_queue_head], sizeof(*event));
        g_queue_head = (g_queue_head + 1u) % TRACE_QUEUE_CAPACITY;
        --g_queue_count;
        found = 1;
    }
    LeaveCriticalSection(&g_queue_lock);
    return found;
}

static int enqueue_control_record(uint32_t kind, uint32_t reason) {
    TraceEvent event;
    LARGE_INTEGER qpc;
    int queued = 0;
    mem_zero(&event, sizeof(event));
    event.magic = TRACE_EVENT_MAGIC;
    event.size = (uint16_t)sizeof(event);
    event.schema_version = TRACE_SCHEMA;
    event.kind = (uint8_t)kind;
    event.tick_ms = GetTickCount();
    event.thread_id = GetCurrentThreadId();
    event.terminal_reason = reason;
    mem_zero(&qpc, sizeof(qpc));
    if (QueryPerformanceCounter(&qpc)) event.qpc = (uint64_t)qpc.QuadPart;
    EnterCriticalSection(&g_queue_lock);
    if (g_queue_count < TRACE_QUEUE_CAPACITY) {
        event.sequence = ++g_next_sequence;
        if (g_queue_count + 1u > g_queue_high_water) g_queue_high_water = g_queue_count + 1u;
        snapshot_counters_locked(&event);
        mem_copy(&g_queue[g_queue_tail], &event, sizeof(event));
        g_queue_tail = (g_queue_tail + 1u) % TRACE_QUEUE_CAPACITY;
        ++g_queue_count;
        queued = 1;
    }
    LeaveCriticalSection(&g_queue_lock);
    if (queued && g_wake_event != NULL) SetEvent(g_wake_event);
    return queued;
}

static int seal_capture_if_drained(uint32_t reason, TraceEvent *terminal) {
    LARGE_INTEGER qpc;
    int sealed = 0;
    mem_zero(terminal, sizeof(*terminal));
    terminal->magic = TRACE_EVENT_MAGIC;
    terminal->size = (uint16_t)sizeof(*terminal);
    terminal->schema_version = TRACE_SCHEMA;
    terminal->kind = TRACE_EVENT_TERMINAL;
    terminal->tick_ms = GetTickCount();
    terminal->thread_id = GetCurrentThreadId();
    terminal->terminal_reason = reason;
    mem_zero(&qpc, sizeof(qpc));
    if (QueryPerformanceCounter(&qpc)) terminal->qpc = (uint64_t)qpc.QuadPart;
    EnterCriticalSection(&g_queue_lock);
    if (InterlockedCompareExchange(&g_accept_new, 0, 0) == 0 &&
        InterlockedCompareExchange(&g_capture_active, 0, 0) != 0 &&
        g_queue_count == 0u && g_inflight_builds == 0u) {
        InterlockedExchange(&g_capture_active, 0);
        terminal->sequence = ++g_next_sequence;
        if (g_queue_high_water == 0u) g_queue_high_water = 1u;
        snapshot_counters_locked(terminal);
        mem_copy(&g_queue[g_queue_tail], terminal, sizeof(*terminal));
        g_queue_tail = (g_queue_tail + 1u) % TRACE_QUEUE_CAPACITY;
        ++g_queue_count;
        sealed = 1;
    }
    LeaveCriticalSection(&g_queue_lock);
    return sealed;
}

static void increment_write_failure(void) {
    EnterCriticalSection(&g_queue_lock);
    ++g_write_failures;
    LeaveCriticalSection(&g_queue_lock);
}

static void refresh_terminal_counters(TraceEvent *event) {
    if (event->kind != TRACE_EVENT_TERMINAL) return;
    EnterCriticalSection(&g_queue_lock);
    snapshot_counters_locked(event);
    LeaveCriticalSection(&g_queue_lock);
}

static void finalize_record_sha256_128(TraceEvent *event) {
    TraceSha256Context context;
    uint8_t digest[32];
    mem_zero(event->record_sha256_128, sizeof(event->record_sha256_128));
    trace_sha256_init(&context);
    trace_sha256_update(&context, (const uint8_t *)event, sizeof(*event));
    trace_sha256_finish(&context, digest);
    mem_copy(event->record_sha256_128, digest, sizeof(event->record_sha256_128));
}

static int write_record(TraceEvent *event) {
    LARGE_INTEGER start;
    uint32_t attempt;
    if (!SetFilePointerEx(g_capture, (LARGE_INTEGER){.QuadPart = 0}, &start, FILE_CURRENT)) {
        increment_write_failure();
        return 0;
    }
    for (attempt = 0u; attempt < TRACE_WRITE_RETRIES; ++attempt) {
        DWORD written = 0u;
        refresh_terminal_counters(event);
        finalize_record_sha256_128(event);
        if (WriteFile(g_capture, event, sizeof(*event), &written, NULL) && written == sizeof(*event)) return 1;
        increment_write_failure();
        if (!SetFilePointerEx(g_capture, start, NULL, FILE_BEGIN) || !SetEndOfFile(g_capture)) return 0;
        Sleep(25u * (attempt + 1u));
    }
    return 0;
}

static void mark_written(const TraceEvent *event) {
    EnterCriticalSection(&g_queue_lock);
    ++g_written_records;
    if (event->kind == TRACE_EVENT_ENTRY) ++g_written_data_records;
    LeaveCriticalSection(&g_queue_lock);
}

static DWORD WINAPI trace_worker(void *unused) {
    char executable_path[1024];
    uint8_t target_sha256[32];
    uint8_t verified_config_sha256[32];
    uint32_t last_flush_tick;
    uint64_t next_checkpoint;
    int write_failed = 0;
    (void)unused;
    if (!make_paths()) {
        signal_startup_complete();
        return 0u;
    }
    /* The first durable lifecycle line is the stop-control discovery anchor.
       Do not install hooks when that control channel cannot be persisted. */
    if (!write_status("INFO", "tracer-starting", g_capture_name)) {
        cleanup_stop_request();
        signal_startup_complete();
        return 0u;
    }
    if (GetPrivateProfileIntA("trace", "enabled", 0, g_config_path) == 0) {
        cleanup_stop_request();
        write_status("INFO", "tracer-disabled", g_capture_name);
        signal_startup_complete();
        return 0u;
    }
    if (!hash_file_sha256(g_config_path, g_config_sha256)) {
        write_status("ERROR", "config-hash-read-failed", g_config_path);
        return fail_startup();
    }
    mem_zero(executable_path, sizeof(executable_path));
    if (GetModuleFileNameA(NULL, executable_path, sizeof(executable_path) - 1u) == 0u ||
        !hash_file_sha256(executable_path, target_sha256)) {
        write_status("ERROR", "target-hash-read-failed", executable_path);
        return fail_startup();
    }
    if (!bytes_equal(target_sha256, kExpectedTargetSha256, 32u)) {
        write_status("ERROR", "target-sha256-mismatch", executable_path);
        return fail_startup();
    }
    g_image_base = (uint8_t *)GetModuleHandleA(NULL);
    if (!verify_image()) {
        write_status("ERROR", "target-pe-identity-mismatch", executable_path);
        return fail_startup();
    }
    if (!IsProcessorFeaturePresent(PF_XMMI_INSTRUCTIONS_AVAILABLE)) {
        write_status("ERROR", "fxsave-processor-feature-unavailable", "hooks not installed");
        return fail_startup();
    }
    if (!IsProcessorFeaturePresent(PF_COMPARE_EXCHANGE_DOUBLE)) {
        write_status("ERROR", "atomic-patch-processor-feature-unavailable", "hooks not installed");
        return fail_startup();
    }
    InitializeCriticalSection(&g_queue_lock);
    g_wake_event = CreateEventA(NULL, FALSE, FALSE, NULL);
    if (g_wake_event == NULL) {
        write_status("ERROR", "wake-event-allocation-failed", "");
        return fail_startup();
    }
    if (!load_config()) return fail_startup();
    if (!hash_file_sha256(g_config_path, verified_config_sha256) ||
        !bytes_equal(g_config_sha256, verified_config_sha256, sizeof(g_config_sha256))) {
        write_status("ERROR", "config-changed-during-resolution", g_config_path);
        return fail_startup();
    }
    if (!validate_all_anchors()) return fail_startup();
    if (InterlockedCompareExchange(&g_stop, 0, 0) != 0) {
        write_status("ERROR", "process-detach-before-hook-install", g_capture_name);
        return fail_startup();
    }
    build_header(target_sha256);
    if (!open_capture()) {
        write_status("ERROR", "capture-open-failed", g_capture_path);
        return fail_startup();
    }
    if (!update_header_state(TRACE_STATE_PATCHING, 1) || !install_hooks_transaction()) {
        (void)update_header_state(TRACE_STATE_FAILED, 1);
        CloseHandle(g_capture);
        g_capture = INVALID_HANDLE_VALUE;
        return fail_startup();
    }
    if (!update_header_state(TRACE_STATE_ACTIVE, 1)) {
        write_status("ERROR", "capture-header-activation-write-failed", g_capture_path);
        InterlockedExchange(&g_stop_reason, TRACE_TERMINAL_WRITE_FAILURE);
        write_failed = 1;
        mark_startup_failed();
    } else {
        InterlockedExchange(&g_accept_new, 1);
        InterlockedExchange(&g_capture_active, 1);
        write_status("INFO", "hooks-active", g_capture_name);
        if (g_zbware_probe_enabled != 0u) run_zbware_direct_probes();
        signal_startup_complete();
    }
    last_flush_tick = GetTickCount();
    next_checkpoint = g_checkpoint_interval;
    while (!write_failed) {
        TraceEvent event;
        int wrote_any = 0;
        if (g_wake_event != NULL) (void)WaitForSingleObject(g_wake_event, 100u);
        if (InterlockedCompareExchange(&g_zbware_memory_probe_observed, 0, 1) == 1) {
            write_status("INFO", "zbware-memory-probe-observed", "hook 55; tracing reset");
        }
        if (InterlockedCompareExchange(&g_zbware_memory_probe_pending, 0, 0) != 0 &&
            GetTickCount() - g_zbware_memory_probe_started_tick >= ZBWARE_PROBE_MEMORY_TIMEOUT_MS) {
            if (InterlockedCompareExchange(&g_zbware_memory_probe_pending, 0, 1) == 1) {
                set_zbware_memory_tracing(0u);
                write_status("WARN", "zbware-memory-probe-timeout", "hook 55 not observed; tracing reset");
            }
        }
        while (pop_queue(&event)) {
            if (!write_record(&event)) {
                write_failed = 1;
                break;
            }
            mark_written(&event);
            wrote_any = 1;
            if (event.kind == TRACE_EVENT_ENTRY && g_written_data_records >= next_checkpoint) {
                if (enqueue_control_record(TRACE_EVENT_CHECKPOINT, TRACE_TERMINAL_NONE)) {
                    next_checkpoint += g_checkpoint_interval;
                }
            }
        }
        if (write_failed) break;
        if (GetTickCount() - last_flush_tick >= g_flush_interval_ms) {
            if (!FlushFileBuffers(g_capture)) {
                increment_write_failure();
                write_failed = 1;
                break;
            }
            last_flush_tick = GetTickCount();
        }
        if (InterlockedCompareExchange(&g_stop, 0, 0) != 0) {
            InterlockedExchange(&g_accept_new, 0);
            InterlockedCompareExchange(&g_stop_reason, TRACE_TERMINAL_PROCESS_DETACH, TRACE_TERMINAL_NONE);
        }
        if (InterlockedCompareExchange(&g_accept_new, 0, 0) != 0 && consume_stop_request()) {
            InterlockedExchange(&g_accept_new, 0);
            InterlockedCompareExchange(&g_stop_reason, TRACE_TERMINAL_STOP_REQUEST, TRACE_TERMINAL_NONE);
            write_status("INFO", "stop-request-accepted", g_capture_name);
        }
        if (InterlockedCompareExchange(&g_accept_new, 0, 0) == 0 &&
            seal_capture_if_drained((uint32_t)InterlockedCompareExchange(&g_stop_reason, 0, 0), &event)) {
            if (!update_header_state(TRACE_STATE_STOPPING, 0)) {
                write_failed = 1;
                break;
            }
            if (!pop_queue(&event) || event.kind != TRACE_EVENT_TERMINAL || !write_record(&event)) {
                write_failed = 1;
                break;
            }
            mark_written(&event);
            break;
        }
        if (!wrote_any && InterlockedCompareExchange(&g_accept_new, 0, 0) == 0 && g_wake_event != NULL) {
            SetEvent(g_wake_event);
        }
    }
    InterlockedExchange(&g_accept_new, 0);
    InterlockedExchange(&g_capture_active, 0);
    if (InterlockedExchange(&g_zbware_memory_probe_pending, 0) != 0) set_zbware_memory_tracing(0u);
    if (write_failed) {
        InterlockedExchange(&g_stop_reason, TRACE_TERMINAL_WRITE_FAILURE);
        (void)update_header_state(TRACE_STATE_FAILED, 1);
        write_status("ERROR", "capture-write-failed-incomplete-prefix", g_capture_name);
    } else {
        if (!FlushFileBuffers(g_capture) || !update_header_state(TRACE_STATE_CLOSED, 1)) {
            increment_write_failure();
            InterlockedExchange(&g_stop_reason, TRACE_TERMINAL_WRITE_FAILURE);
            (void)update_header_state(TRACE_STATE_FAILED, 1);
            write_status("ERROR", "capture-finalization-failed", g_capture_name);
        } else {
            write_status("INFO", "capture-closed", g_capture_name);
        }
    }
    cleanup_stop_request();
    CloseHandle(g_capture);
    g_capture = INVALID_HANDLE_VALUE;
    return 0u;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_module = instance;
        DisableThreadLibraryCalls(instance);
    } else if (reason == DLL_PROCESS_DETACH) {
        InterlockedExchange(&g_accept_new, 0);
        InterlockedExchange(&g_stop, 1);
        InterlockedCompareExchange(&g_stop_reason, TRACE_TERMINAL_PROCESS_DETACH, TRACE_TERMINAL_NONE);
        if (g_wake_event != NULL) SetEvent(g_wake_event);
    }
    return TRUE;
}

__declspec(dllexport) void __cdecl InitializeASI(void) {
    LONG prior_state = InterlockedCompareExchange(&g_initialize_state, 1, 0);
    if (prior_state == 0) {
        HANDLE thread;
        HMODULE pinned = NULL;
        if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                                (LPCSTR)(uintptr_t)&InitializeASI, &pinned)) {
            InterlockedExchange(&g_initialize_state, 2);
            return;
        }
        g_startup_event = CreateEventA(NULL, TRUE, FALSE, NULL);
        if (g_startup_event == NULL) {
            InterlockedExchange(&g_initialize_state, 2);
            return;
        }
        thread = CreateThread(NULL, 0u, trace_worker, NULL, 0u, NULL);
        if (thread == NULL) {
            InterlockedExchange(&g_initialize_state, 2);
            SetEvent(g_startup_event);
            return;
        }
        CloseHandle(thread);
    }
    for (;;) {
        HANDLE startup_event;
        if (InterlockedCompareExchange(&g_initialize_state, 0, 0) == 2) return;
        startup_event = g_startup_event;
        if (startup_event != NULL) {
            (void)WaitForSingleObject(startup_event, INFINITE);
            return;
        }
        Sleep(0u);
    }
}
