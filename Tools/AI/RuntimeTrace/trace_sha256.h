#ifndef NFSMW_AI_TRACE_SHA256_H
#define NFSMW_AI_TRACE_SHA256_H

#include <stdint.h>

typedef struct TraceSha256Context {
    uint8_t data[64];
    uint32_t data_len;
    uint32_t state[8];
    uint32_t bit_len_low;
    uint32_t bit_len_high;
} TraceSha256Context;

static uint32_t trace_sha256_rotr32(uint32_t value, uint32_t bits) {
    return (value >> bits) | (value << (32u - bits));
}

static void trace_sha256_transform(TraceSha256Context *context, const uint8_t data[64]) {
    static const uint32_t constants[64] = {
        0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
        0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
        0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
        0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
        0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
        0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
        0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
        0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u,
    };
    uint32_t words[64];
    uint32_t a, b, c, d, e, f, g, h, s0, s1, choose, majority, temp1, temp2;
    uint32_t index;
    for (index = 0u; index < 16u; ++index) {
        words[index] = ((uint32_t)data[index * 4u] << 24u) | ((uint32_t)data[index * 4u + 1u] << 16u) |
                       ((uint32_t)data[index * 4u + 2u] << 8u) | (uint32_t)data[index * 4u + 3u];
    }
    for (index = 16u; index < 64u; ++index) {
        s0 = trace_sha256_rotr32(words[index - 15u], 7u) ^ trace_sha256_rotr32(words[index - 15u], 18u) ^
             (words[index - 15u] >> 3u);
        s1 = trace_sha256_rotr32(words[index - 2u], 17u) ^ trace_sha256_rotr32(words[index - 2u], 19u) ^
             (words[index - 2u] >> 10u);
        words[index] = words[index - 16u] + s0 + words[index - 7u] + s1;
    }
    a = context->state[0]; b = context->state[1]; c = context->state[2]; d = context->state[3];
    e = context->state[4]; f = context->state[5]; g = context->state[6]; h = context->state[7];
    for (index = 0u; index < 64u; ++index) {
        s1 = trace_sha256_rotr32(e, 6u) ^ trace_sha256_rotr32(e, 11u) ^ trace_sha256_rotr32(e, 25u);
        choose = (e & f) ^ ((~e) & g);
        temp1 = h + s1 + choose + constants[index] + words[index];
        s0 = trace_sha256_rotr32(a, 2u) ^ trace_sha256_rotr32(a, 13u) ^ trace_sha256_rotr32(a, 22u);
        majority = (a & b) ^ (a & c) ^ (b & c);
        temp2 = s0 + majority;
        h = g; g = f; f = e; e = d + temp1; d = c; c = b; b = a; a = temp1 + temp2;
    }
    context->state[0] += a; context->state[1] += b; context->state[2] += c; context->state[3] += d;
    context->state[4] += e; context->state[5] += f; context->state[6] += g; context->state[7] += h;
}

static void trace_sha256_init(TraceSha256Context *context) {
    uint32_t index;
    uint8_t *raw = (uint8_t *)context;
    for (index = 0u; index < (uint32_t)sizeof(*context); ++index) raw[index] = 0u;
    context->state[0] = 0x6a09e667u; context->state[1] = 0xbb67ae85u;
    context->state[2] = 0x3c6ef372u; context->state[3] = 0xa54ff53au;
    context->state[4] = 0x510e527fu; context->state[5] = 0x9b05688cu;
    context->state[6] = 0x1f83d9abu; context->state[7] = 0x5be0cd19u;
}

static void trace_sha256_add_bits(TraceSha256Context *context, uint32_t byte_count) {
    uint32_t add_low = byte_count << 3u;
    uint32_t old_low = context->bit_len_low;
    context->bit_len_low += add_low;
    if (context->bit_len_low < old_low) ++context->bit_len_high;
    context->bit_len_high += byte_count >> 29u;
}

static void trace_sha256_update(TraceSha256Context *context, const uint8_t *data, uint32_t size) {
    uint32_t index;
    trace_sha256_add_bits(context, size);
    for (index = 0u; index < size; ++index) {
        context->data[context->data_len++] = data[index];
        if (context->data_len == 64u) {
            trace_sha256_transform(context, context->data);
            context->data_len = 0u;
        }
    }
}

static void trace_sha256_finish(TraceSha256Context *context, uint8_t output[32]) {
    uint32_t index = context->data_len;
    context->data[index++] = 0x80u;
    if (index > 56u) {
        while (index < 64u) context->data[index++] = 0u;
        trace_sha256_transform(context, context->data);
        index = 0u;
    }
    while (index < 56u) context->data[index++] = 0u;
    context->data[56] = (uint8_t)(context->bit_len_high >> 24u);
    context->data[57] = (uint8_t)(context->bit_len_high >> 16u);
    context->data[58] = (uint8_t)(context->bit_len_high >> 8u);
    context->data[59] = (uint8_t)context->bit_len_high;
    context->data[60] = (uint8_t)(context->bit_len_low >> 24u);
    context->data[61] = (uint8_t)(context->bit_len_low >> 16u);
    context->data[62] = (uint8_t)(context->bit_len_low >> 8u);
    context->data[63] = (uint8_t)context->bit_len_low;
    trace_sha256_transform(context, context->data);
    for (index = 0u; index < 8u; ++index) {
        output[index * 4u] = (uint8_t)(context->state[index] >> 24u);
        output[index * 4u + 1u] = (uint8_t)(context->state[index] >> 16u);
        output[index * 4u + 2u] = (uint8_t)(context->state[index] >> 8u);
        output[index * 4u + 3u] = (uint8_t)context->state[index];
    }
}

#endif
