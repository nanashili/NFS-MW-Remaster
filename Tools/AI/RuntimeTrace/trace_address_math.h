#ifndef NFSMW_AI_TRACE_ADDRESS_MATH_H
#define NFSMW_AI_TRACE_ADDRESS_MATH_H

#include <stdint.h>

/* The exclusive end may be 2^32 for a span ending at address 0xffffffff. */
static int trace_checked_u32_span(uint32_t address, uint32_t size, uint64_t *end_exclusive) {
    uint64_t end;
    if (size == 0u || end_exclusive == (uint64_t *)0) return 0;
    end = (uint64_t)address + (uint64_t)size;
    if (end > UINT64_C(0x100000000)) return 0;
    *end_exclusive = end;
    return 1;
}

#endif
