#ifndef NFSMW_AI_TRACE_CONFIG_PARSER_H
#define NFSMW_AI_TRACE_CONFIG_PARSER_H

#include <stdint.h>

static int trace_parse_u32(const char *text, uint32_t *output) {
    uint32_t value = 0u;
    uint32_t index = 0u;
    if (text == (const char *)0 || text[0] == '\0' || output == (uint32_t *)0) return 0;
    while (text[index] != '\0') {
        uint32_t digit;
        if (text[index] < '0' || text[index] > '9') return 0;
        digit = (uint32_t)(text[index] - '0');
        if (value > (0xffffffffu - digit) / 10u) return 0;
        value = value * 10u + digit;
        ++index;
    }
    *output = value;
    return 1;
}

#endif
