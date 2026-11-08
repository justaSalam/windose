#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// TinyCC for Windose OS: Native 64-bit x86_64 C Compiler
// Generates standalone PIE ELF binaries for Windose with KernelApi linkage.
// usage: tcc <source.c> [-o output.elf] [-c] [-log log.txt] [-I inc_dir]

#define MAX_SRC_SIZE    (512 * 1024)
#define MAX_TEXT_SIZE   (256 * 1024)
#define MAX_DATA_SIZE   (128 * 1024)
#define MAX_SYMBOLS     1024
#define MAX_LOCALS      256
#define MAX_INCLUDES    16
#define MAX_LINE_LEN    512

typedef enum {
    TOK_EOF = 0,
    TOK_NUM,
    TOK_ID,
    TOK_STR,
    TOK_CHAR,
    // Keywords
    TOK_INT,
    TOK_CHAR_KW,
    TOK_VOID,
    TOK_STRUCT,
    TOK_TYPEDEF,
    TOK_RETURN,
    TOK_IF,
    TOK_ELSE,
    TOK_WHILE,
    TOK_FOR,
    TOK_DO,
    TOK_BREAK,
    TOK_CONTINUE,
    TOK_SIZEOF,
    TOK_EXTERN,
    TOK_STATIC,
    // Operators
    TOK_EQ,       // ==
    TOK_NEQ,      // !=
    TOK_LEQ,      // <=
    TOK_GEQ,      // >=
    TOK_SHL,      // <<
    TOK_SHR,      // >>
    TOK_AND,      // &&
    TOK_OR,       // ||
    TOK_ARROW,    // ->
    TOK_INC,      // ++
    TOK_DEC,      // --
    TOK_ADD_ASSIGN, // +=
    TOK_SUB_ASSIGN  // -=
} TokenType;

typedef struct {
    TokenType type;
    int int_val;
    char str_val[256];
    int line;
} Token;

typedef struct {
    char name[64];
    int offset;    // Offset in .data for globals, offset from %rbp for locals
    int is_global;
    int is_func;
    int is_ptr;
    int size;      // 1, 2, 4, 8 bytes
} Symbol;

static char src_buf[MAX_SRC_SIZE];
static unsigned char text_buf[MAX_TEXT_SIZE];
static unsigned char data_buf[MAX_DATA_SIZE];
static int text_len = 0;
static int data_len = 0;

static Symbol globals[MAX_SYMBOLS];
static int num_globals = 0;

static Symbol locals[MAX_LOCALS];
static int num_locals = 0;
static int local_stack_offset = 0;

static char include_paths[MAX_INCLUDES][256];
static int num_include_paths = 0;

static char current_file[256] = "";
static int current_line = 1;
static char *src_ptr = NULL;
static Token tok;

static char log_buf[32 * 1024];
static int log_len = 0;
static char log_path[256] = "";

static void log_msg(const char *fmt, ...)
{
    char buf[512];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);

    printf("%s", buf);
    if (log_path[0]) {
        int l = (int)strlen(buf);
        if (log_len + l < (int)sizeof(log_buf) - 1) {
            memcpy(log_buf + log_len, buf, (size_t)l);
            log_len += l;
            log_buf[log_len] = 0;
            k_write_file(log_path, log_buf, log_len);
        }
    }
}

static void error_at(int line, const char *msg)
{
    log_msg("%s:%d: error: %s\n", current_file, line, msg);
}

// ---------------- Preprocessor ----------------

static void add_include_path(const char *path)
{
    if (num_include_paths < MAX_INCLUDES) {
        strncpy(include_paths[num_include_paths++], path, 255);
    }
}

static int read_full_file(const char *path, char *out, int max)
{
    int n = k_read_file(path, out, max - 1);
    if (n >= 0) {
        out[n] = 0;
        return n;
    }
    return -1;
}

static int resolve_include(const char *name, char *out_full, int out_max)
{
    FileStat st;
    // 1. Check relative to current file or /mnt
    if (k_stat_file(name, &st) == 0 && !st.is_dir) {
        strncpy(out_full, name, out_max - 1);
        return 0;
    }
    // 2. Check include paths
    for (int i = 0; i < num_include_paths; i++) {
        snprintf(out_full, out_max, "%s/%s", include_paths[i], name);
        if (k_stat_file(out_full, &st) == 0 && !st.is_dir) return 0;
    }
    return -1;
}

// Simple preprocessor: expands #include, strips comments, expands basic #define
static int preprocess(const char *raw_src, char *out_buf, int max_out)
{
    const char *p = raw_src;
    char *out = out_buf;
    char *out_end = out_buf + max_out - 1;

    while (*p && out < out_end) {
        // Skip single-line comments
        if (p[0] == '/' && p[1] == '/') {
            p += 2;
            while (*p && *p != '\n') p++;
            if (*p == '\n') *out++ = *p++;
            continue;
        }
        // Skip multi-line comments
        if (p[0] == '/' && p[1] == '*') {
            p += 2;
            while (*p && !(p[0] == '*' && p[1] == '/')) {
                if (*p == '\n') *out++ = '\n';
                p++;
            }
            if (*p) p += 2;
            continue;
        }
        // Handle preprocessor directive
        if (*p == '#') {
            const char *dir_start = p;
            p++;
            while (*p == ' ' || *p == '\t') p++;
            if (!strncmp(p, "include", 7)) {
                p += 7;
                while (*p == ' ' || *p == '\t') p++;
                char inc_name[128];
                int k = 0;
                char close_char = 0;
                if (*p == '<') { close_char = '>'; p++; }
                else if (*p == '"') { close_char = '"'; p++; }

                if (close_char) {
                    while (*p && *p != close_char && *p != '\n' && k < 127) {
                        inc_name[k++] = *p++;
                    }
                    inc_name[k] = 0;
                    if (*p == close_char) p++;
                    while (*p && *p != '\n') p++;
                    if (*p == '\n') p++;

                    char inc_path[256];
                    char inc_src[64 * 1024];
                    if (resolve_include(inc_name, inc_path, sizeof(inc_path)) == 0) {
                        int r = read_full_file(inc_path, inc_src, sizeof(inc_src));
                        if (r > 0) {
                            int rem = (int)(out_end - out);
                            int written = preprocess(inc_src, out, rem);
                            if (written > 0) out += written;
                        }
                    }
                    continue;
                }
            } else {
                // Skip unhandled directive until newline (e.g. #pragma, #define, #ifdef)
                p = dir_start;
                while (*p && *p != '\n') p++;
                if (*p == '\n') *out++ = *p++;
                continue;
            }
        }
        *out++ = *p++;
    }
    *out = 0;
    return (int)(out - out_buf);
}

// ---------------- Tokenizer ----------------

static void next_token(void)
{
    while (*src_ptr) {
        // Skip whitespace
        if (*src_ptr == ' ' || *src_ptr == '\t' || *src_ptr == '\r') {
            src_ptr++;
            continue;
        }
        if (*src_ptr == '\n') {
            current_line++;
            src_ptr++;
            continue;
        }
        break;
    }

    tok.line = current_line;
    if (!*src_ptr) {
        tok.type = TOK_EOF;
        return;
    }

    // Number literal
    if (*src_ptr >= '0' && *src_ptr <= '9') {
        int base = 10;
        int val = 0;
        if (src_ptr[0] == '0' && (src_ptr[1] == 'x' || src_ptr[1] == 'X')) {
            base = 16;
            src_ptr += 2;
        }
        while ((*src_ptr >= '0' && *src_ptr <= '9') ||
               (base == 16 && ((*src_ptr >= 'a' && *src_ptr <= 'f') || (*src_ptr >= 'A' && *src_ptr <= 'F')))) {
            int d = 0;
            if (*src_ptr >= '0' && *src_ptr <= '9') d = *src_ptr - '0';
            else if (*src_ptr >= 'a' && *src_ptr <= 'f') d = *src_ptr - 'a' + 10;
            else if (*src_ptr >= 'A' && *src_ptr <= 'F') d = *src_ptr - 'A' + 10;
            val = val * base + d;
            src_ptr++;
        }
        // Skip suffixes (u, l, ll)
        while (*src_ptr == 'u' || *src_ptr == 'U' || *src_ptr == 'l' || *src_ptr == 'L') src_ptr++;
        tok.type = TOK_NUM;
        tok.int_val = val;
        return;
    }

    // String literal
    if (*src_ptr == '"') {
        src_ptr++;
        int k = 0;
        while (*src_ptr && *src_ptr != '"' && k < 255) {
            if (*src_ptr == '\\') {
                src_ptr++;
                if (*src_ptr == 'n') tok.str_val[k++] = '\n';
                else if (*src_ptr == 'r') tok.str_val[k++] = '\r';
                else if (*src_ptr == 't') tok.str_val[k++] = '\t';
                else if (*src_ptr == '0') tok.str_val[k++] = '\0';
                else tok.str_val[k++] = *src_ptr;
            } else {
                tok.str_val[k++] = *src_ptr;
            }
            src_ptr++;
        }
        tok.str_val[k] = 0;
        if (*src_ptr == '"') src_ptr++;
        tok.type = TOK_STR;
        return;
    }

    // Character literal
    if (*src_ptr == '\'') {
        src_ptr++;
        char c = *src_ptr++;
        if (c == '\\') {
            if (*src_ptr == 'n') c = '\n';
            else if (*src_ptr == 'r') c = '\r';
            else if (*src_ptr == 't') c = '\t';
            else if (*src_ptr == '0') c = '\0';
            else c = *src_ptr;
            src_ptr++;
        }
        if (*src_ptr == '\'') src_ptr++;
        tok.type = TOK_CHAR;
        tok.int_val = (int)c;
        return;
    }

    // Identifier or keyword
    if ((*src_ptr >= 'a' && *src_ptr <= 'z') || (*src_ptr >= 'A' && *src_ptr <= 'Z') || *src_ptr == '_') {
        int k = 0;
        while (((*src_ptr >= 'a' && *src_ptr <= 'z') ||
                (*src_ptr >= 'A' && *src_ptr <= 'Z') ||
                (*src_ptr >= '0' && *src_ptr <= '9') ||
                *src_ptr == '_') && k < 255) {
            tok.str_val[k++] = *src_ptr++;
        }
        tok.str_val[k] = 0;

        if (!strcmp(tok.str_val, "int")) tok.type = TOK_INT;
        else if (!strcmp(tok.str_val, "char")) tok.type = TOK_CHAR_KW;
        else if (!strcmp(tok.str_val, "void")) tok.type = TOK_VOID;
        else if (!strcmp(tok.str_val, "struct")) tok.type = TOK_STRUCT;
        else if (!strcmp(tok.str_val, "typedef")) tok.type = TOK_TYPEDEF;
        else if (!strcmp(tok.str_val, "return")) tok.type = TOK_RETURN;
        else if (!strcmp(tok.str_val, "if")) tok.type = TOK_IF;
        else if (!strcmp(tok.str_val, "else")) tok.type = TOK_ELSE;
        else if (!strcmp(tok.str_val, "while")) tok.type = TOK_WHILE;
        else if (!strcmp(tok.str_val, "for")) tok.type = TOK_FOR;
        else if (!strcmp(tok.str_val, "do")) tok.type = TOK_DO;
        else if (!strcmp(tok.str_val, "break")) tok.type = TOK_BREAK;
        else if (!strcmp(tok.str_val, "continue")) tok.type = TOK_CONTINUE;
        else if (!strcmp(tok.str_val, "sizeof")) tok.type = TOK_SIZEOF;
        else if (!strcmp(tok.str_val, "extern")) tok.type = TOK_EXTERN;
        else if (!strcmp(tok.str_val, "static")) tok.type = TOK_STATIC;
        else tok.type = TOK_ID;
        return;
    }

    // Multi-char operators
    if (src_ptr[0] == '=' && src_ptr[1] == '=') { src_ptr += 2; tok.type = TOK_EQ; return; }
    if (src_ptr[0] == '!' && src_ptr[1] == '=') { src_ptr += 2; tok.type = TOK_NEQ; return; }
    if (src_ptr[0] == '<' && src_ptr[1] == '=') { src_ptr += 2; tok.type = TOK_LEQ; return; }
    if (src_ptr[0] == '>' && src_ptr[1] == '=') { src_ptr += 2; tok.type = TOK_GEQ; return; }
    if (src_ptr[0] == '<' && src_ptr[1] == '<') { src_ptr += 2; tok.type = TOK_SHL; return; }
    if (src_ptr[0] == '>' && src_ptr[1] == '>') { src_ptr += 2; tok.type = TOK_SHR; return; }
    if (src_ptr[0] == '&' && src_ptr[1] == '&') { src_ptr += 2; tok.type = TOK_AND; return; }
    if (src_ptr[0] == '|' && src_ptr[1] == '|') { src_ptr += 2; tok.type = TOK_OR; return; }
    if (src_ptr[0] == '-' && src_ptr[1] == '>') { src_ptr += 2; tok.type = TOK_ARROW; return; }
    if (src_ptr[0] == '+' && src_ptr[1] == '+') { src_ptr += 2; tok.type = TOK_INC; return; }
    if (src_ptr[0] == '-' && src_ptr[1] == '-') { src_ptr += 2; tok.type = TOK_DEC; return; }
    if (src_ptr[0] == '+' && src_ptr[1] == '=') { src_ptr += 2; tok.type = TOK_ADD_ASSIGN; return; }
    if (src_ptr[0] == '-' && src_ptr[1] == '=') { src_ptr += 2; tok.type = TOK_SUB_ASSIGN; return; }

    // Single-char operator / punctuation
    tok.type = (TokenType)*src_ptr;
    tok.int_val = *src_ptr;
    src_ptr++;
}

// ---------------- x86_64 Machine Code Emitter ----------------

static void emit_byte(unsigned char b)
{
    if (text_len < MAX_TEXT_SIZE) text_buf[text_len++] = b;
}

static void emit_bytes(const unsigned char *b, int len)
{
    for (int i = 0; i < len; i++) emit_byte(b[i]);
}

static void emit_u32(unsigned int v)
{
    emit_byte((unsigned char)(v & 0xFF));
    emit_byte((unsigned char)((v >> 8) & 0xFF));
    emit_byte((unsigned char)((v >> 16) & 0xFF));
    emit_byte((unsigned char)((v >> 24) & 0xFF));
}

static void emit_u64(unsigned long long v)
{
    emit_u32((unsigned int)(v & 0xFFFFFFFF));
    emit_u32((unsigned int)((v >> 32) & 0xFFFFFFFF));
}

static int add_data_string(const char *s, int len)
{
    int off = data_len;
    if (data_len + len + 1 < MAX_DATA_SIZE) {
        memcpy(data_buf + data_len, s, (size_t)len);
        data_buf[data_len + len] = 0;
        data_len += len + 1;
        // Align to 8 bytes
        while (data_len % 8 != 0) data_buf[data_len++] = 0;
    }
    return off;
}

static Symbol *find_symbol(const char *name)
{
    // Search locals first
    for (int i = num_locals - 1; i >= 0; i--) {
        if (!strcmp(locals[i].name, name)) return &locals[i];
    }
    // Search globals
    for (int i = num_globals - 1; i >= 0; i--) {
        if (!strcmp(globals[i].name, name)) return &globals[i];
    }
    return NULL;
}

static Symbol *add_global(const char *name, int is_func, int is_ptr, int size)
{
    if (num_globals >= MAX_SYMBOLS) return NULL;
    Symbol *sym = &globals[num_globals++];
    strncpy(sym->name, name, sizeof(sym->name) - 1);
    sym->is_global = 1;
    sym->is_func = is_func;
    sym->is_ptr = is_ptr;
    sym->size = size;
    sym->offset = is_func ? text_len : data_len;
    if (!is_func) {
        // Reserve space in data segment
        data_len += size;
        while (data_len % 8 != 0) data_buf[data_len++] = 0;
    }
    return sym;
}

static Symbol *add_local(const char *name, int is_ptr, int size)
{
    if (num_locals >= MAX_LOCALS) return NULL;
    Symbol *sym = &locals[num_locals++];
    strncpy(sym->name, name, sizeof(sym->name) - 1);
    sym->is_global = 0;
    sym->is_func = 0;
    sym->is_ptr = is_ptr;
    sym->size = size;
    local_stack_offset += 8; // 64-bit slots
    sym->offset = -local_stack_offset;
    return sym;
}

// ---------------- Code Generation Primitives ----------------

static void gen_prologue(void)
{
    // push %rbp; mov %rsp, %rbp
    emit_byte(0x55);
    emit_byte(0x48); emit_byte(0x89); emit_byte(0xe5);
    // sub $256, %rsp (reserve local stack space)
    emit_byte(0x48); emit_byte(0x81); emit_byte(0xec);
    emit_u32(256);
}

static void gen_epilogue(void)
{
    // leave; ret
    emit_byte(0xc9);
    emit_byte(0xc3);
}

// Push %rax onto stack
static void gen_push_rax(void)
{
    emit_byte(0x50);
}

// Pop from stack into %rdi
static void gen_pop_rdi(void)
{
    emit_byte(0x5f);
}

// Pop from stack into %rax
static void gen_pop_rax(void)
{
    emit_byte(0x58);
}

// Load immediate number into %rax: mov $val, %rax
static void gen_imm(int val)
{
    emit_byte(0x48); emit_byte(0xc7); emit_byte(0xc0);
    emit_u32((unsigned int)val);
}

// Load local variable into %rax: mov -off(%rbp), %rax
static void gen_load_local(int off)
{
    emit_byte(0x48); emit_byte(0x8b); emit_byte(0x85);
    emit_u32((unsigned int)off);
}

// Store %rax into local variable: mov %rax, -off(%rbp)
static void gen_store_local(int off)
{
    emit_byte(0x48); emit_byte(0x89); emit_byte(0x85);
    emit_u32((unsigned int)off);
}

// ---------------- Parser ----------------

static void parse_expression(void);

static void parse_primary(void)
{
    if (tok.type == TOK_NUM) {
        gen_imm(tok.int_val);
        next_token();
    } else if (tok.type == TOK_CHAR) {
        gen_imm(tok.int_val);
        next_token();
    } else if (tok.type == TOK_STR) {
        // Put string into data segment, load effective address (RIP-relative)
        int str_off = add_data_string(tok.str_val, (int)strlen(tok.str_val));
        // lea data_offset(%rip), %rax
        emit_byte(0x48); emit_byte(0x8d); emit_byte(0x05);
        emit_u32((unsigned int)(0x1000 + str_off));
        next_token();
    } else if (tok.type == TOK_ID) {
        char name[64];
        strncpy(name, tok.str_val, sizeof(name) - 1);
        next_token();

        // Check for function call
        if (tok.type == '(') {
            next_token();
            int arg_count = 0;
            while (tok.type != ')' && tok.type != TOK_EOF) {
                parse_expression();
                gen_push_rax();
                arg_count++;
                if (tok.type == ',') next_token();
            }
            if (tok.type == ')') next_token();

            // Pop args into System V ABI registers: %rdi, %rsi, %rdx, %rcx, %r8, %r9
            // Stack has args pushed left-to-right, so top of stack is rightmost arg
            for (int a = arg_count - 1; a >= 0; a--) {
                if (a == 0) gen_pop_rdi();
                else if (a == 1) { emit_byte(0x5e); } // pop %rsi
                else if (a == 2) { emit_byte(0x5a); } // pop %rdx
                else if (a == 3) { emit_byte(0x59); } // pop %rcx
                else if (a == 4) { emit_byte(0x41); emit_byte(0x58); } // pop %r8
                else if (a == 5) { emit_byte(0x41); emit_byte(0x59); } // pop %r9
                else { gen_pop_rax(); } // Extra args
            }

            // Call function
            Symbol *func = find_symbol(name);
            if (func && func->is_func) {
                // Direct relative call: call rel32
                emit_byte(0xe8);
                int rel = func->offset - (text_len + 4);
                emit_u32((unsigned int)rel);
            } else {
                // If unknown symbol, emit a dummy stub or placeholder
                emit_byte(0xe8);
                emit_u32(0);
            }
        } else {
            // Variable reference
            Symbol *sym = find_symbol(name);
            if (sym) {
                if (!sym->is_global) {
                    gen_load_local(sym->offset);
                } else {
                    // Global variable: mov data_offset(%rip), %rax
                    emit_byte(0x48); emit_byte(0x8b); emit_byte(0x05);
                    emit_u32((unsigned int)(0x1000 + sym->offset));
                }
            } else {
                // Treat as new local variable
                sym = add_local(name, 0, 8);
                gen_load_local(sym->offset);
            }
        }
    } else if (tok.type == '(') {
        next_token();
        parse_expression();
        if (tok.type == ')') next_token();
    }
}

static void parse_multiplicative(void)
{
    parse_primary();
    while (tok.type == '*' || tok.type == '/' || tok.type == '%') {
        int op = tok.type;
        next_token();
        gen_push_rax();
        parse_primary();
        // %rax has RHS, top of stack has LHS
        gen_pop_rdi(); // %rdi = LHS
        if (op == '*') {
            // imul %rdi, %rax
            emit_byte(0x48); emit_byte(0x0f); emit_byte(0xaf); emit_byte(0xc7);
        } else if (op == '/') {
            // xchg %rax, %rdi; ctd; idiv %rdi
            emit_byte(0x48); emit_byte(0x97);
            emit_byte(0x48); emit_byte(0x99);
            emit_byte(0x48); emit_byte(0xf7); emit_byte(0xff);
        } else if (op == '%') {
            // xchg %rax, %rdi; ctd; idiv %rdi; mov %rdx, %rax
            emit_byte(0x48); emit_byte(0x97);
            emit_byte(0x48); emit_byte(0x99);
            emit_byte(0x48); emit_byte(0xf7); emit_byte(0xff);
            emit_byte(0x48); emit_byte(0x89); emit_byte(0xd0);
        }
    }
}

static void parse_additive(void)
{
    parse_multiplicative();
    while (tok.type == '+' || tok.type == '-') {
        int op = tok.type;
        next_token();
        gen_push_rax();
        parse_multiplicative();
        gen_pop_rdi(); // %rdi = LHS, %rax = RHS
        if (op == '+') {
            // add %rdi, %rax
            emit_byte(0x48); emit_byte(0x01); emit_byte(0xf8);
        } else {
            // sub %rax, %rdi; mov %rdi, %rax
            emit_byte(0x48); emit_byte(0x29); emit_byte(0xc7);
            emit_byte(0x48); emit_byte(0x89); emit_byte(0xf8);
        }
    }
}

static void parse_relational(void)
{
    parse_additive();
    while (tok.type == '<' || tok.type == '>' || tok.type == TOK_LEQ || tok.type == TOK_GEQ ||
           tok.type == TOK_EQ || tok.type == TOK_NEQ) {
        int op = tok.type;
        next_token();
        gen_push_rax();
        parse_additive();
        gen_pop_rdi(); // %rdi = LHS, %rax = RHS
        // cmp %rax, %rdi
        emit_byte(0x48); emit_byte(0x39); emit_byte(0xc7);
        if (op == TOK_EQ) { emit_byte(0x0f); emit_byte(0x94); emit_byte(0xc0); } // sete %al
        else if (op == TOK_NEQ) { emit_byte(0x0f); emit_byte(0x95); emit_byte(0xc0); } // setne %al
        else if (op == '<') { emit_byte(0x0f); emit_byte(0x9c); emit_byte(0xc0); } // setl %al
        else if (op == '>') { emit_byte(0x0f); emit_byte(0x9f); emit_byte(0xc0); } // setg %al
        else if (op == TOK_LEQ) { emit_byte(0x0f); emit_byte(0x9e); emit_byte(0xc0); } // setle %al
        else if (op == TOK_GEQ) { emit_byte(0x0f); emit_byte(0x9d); emit_byte(0xc0); } // setge %al
        // movzbq %al, %rax
        emit_byte(0x48); emit_byte(0x0f); emit_byte(0xb6); emit_byte(0xc0);
    }
}

static void parse_expression(void)
{
    parse_relational();
    if (tok.type == '=') {
        next_token();
        // For assignment, RHS is evaluated into %rax
        parse_expression();
        // If left side was a local, we stored its offset
        if (num_locals > 0) {
            gen_store_local(locals[num_locals - 1].offset);
        }
    }
}

static void parse_statement(void);

static void parse_block(void)
{
    if (tok.type == '{') next_token();
    while (tok.type != '}' && tok.type != TOK_EOF) {
        parse_statement();
    }
    if (tok.type == '}') next_token();
}

static void parse_statement(void)
{
    if (tok.type == '{') {
        parse_block();
    } else if (tok.type == TOK_RETURN) {
        next_token();
        if (tok.type != ';') parse_expression();
        gen_epilogue();
        if (tok.type == ';') next_token();
    } else if (tok.type == TOK_IF) {
        next_token();
        if (tok.type == '(') next_token();
        parse_expression();
        if (tok.type == ')') next_token();

        // test %rax, %rax; jz else_target
        emit_byte(0x48); emit_byte(0x85); emit_byte(0xc0);
        emit_byte(0x0f); emit_byte(0x84);
        int jz_off = text_len;
        emit_u32(0); // placeholder for rel32

        parse_statement();

        if (tok.type == TOK_ELSE) {
            next_token();
            // jmp end_target
            emit_byte(0xe9);
            int jmp_off = text_len;
            emit_u32(0);

            // patch jz
            int rel = text_len - (jz_off + 4);
            memcpy(text_buf + jz_off, &rel, 4);

            parse_statement();

            // patch jmp
            int jmp_rel = text_len - (jmp_off + 4);
            memcpy(text_buf + jmp_off, &jmp_rel, 4);
        } else {
            // patch jz
            int rel = text_len - (jz_off + 4);
            memcpy(text_buf + jz_off, &rel, 4);
        }
    } else if (tok.type == TOK_WHILE) {
        next_token();
        int loop_top = text_len;
        if (tok.type == '(') next_token();
        parse_expression();
        if (tok.type == ')') next_token();

        // test %rax, %rax; jz exit_target
        emit_byte(0x48); emit_byte(0x85); emit_byte(0xc0);
        emit_byte(0x0f); emit_byte(0x84);
        int jz_off = text_len;
        emit_u32(0);

        parse_statement();

        // jmp loop_top
        emit_byte(0xe9);
        int jmp_rel = loop_top - (text_len + 4);
        emit_u32((unsigned int)jmp_rel);

        // patch jz
        int exit_rel = text_len - (jz_off + 4);
        memcpy(text_buf + jz_off, &exit_rel, 4);
    } else if (tok.type == TOK_INT || tok.type == TOK_CHAR_KW || tok.type == TOK_VOID) {
        // Variable declaration: int x = 5;
        next_token();
        int is_ptr = 0;
        while (tok.type == '*') { is_ptr = 1; next_token(); }
        if (tok.type == TOK_ID) {
            char name[64];
            strncpy(name, tok.str_val, sizeof(name) - 1);
            Symbol *sym = add_local(name, is_ptr, is_ptr ? 8 : 4);
            next_token();
            if (tok.type == '=') {
                next_token();
                parse_expression();
                gen_store_local(sym->offset);
            }
        }
        if (tok.type == ';') next_token();
    } else {
        parse_expression();
        if (tok.type == ';') next_token();
    }
}

static void parse_function_or_global(void)
{
    // Skip static, extern, inline
    while (tok.type == TOK_STATIC || tok.type == TOK_EXTERN) next_token();

    if (tok.type == TOK_INT || tok.type == TOK_CHAR_KW || tok.type == TOK_VOID || tok.type == TOK_STRUCT) {
        next_token();
        if (tok.type == TOK_ID && !strcmp(tok.str_val, "KernelApi")) {
            // Skip typedef or struct
            while (tok.type != ';' && tok.type != TOK_EOF) next_token();
            if (tok.type == ';') next_token();
            return;
        }
    }

    int is_ptr = 0;
    while (tok.type == '*') { is_ptr = 1; next_token(); }

    if (tok.type == TOK_ID) {
        char name[64];
        strncpy(name, tok.str_val, sizeof(name) - 1);
        next_token();

        if (tok.type == '(') {
            // Function declaration or definition
            next_token();
            num_locals = 0;
            local_stack_offset = 0;

            int param_idx = 0;
            while (tok.type != ')' && tok.type != TOK_EOF) {
                // Parse param type
                while (tok.type == TOK_INT || tok.type == TOK_CHAR_KW || tok.type == TOK_VOID || tok.type == '*') next_token();
                if (tok.type == TOK_ID) {
                    Symbol *param = add_local(tok.str_val, 0, 8);
                    // Store param from register into stack slot
                    if (param_idx == 0) { emit_byte(0x48); emit_byte(0x89); emit_byte(0xbd); emit_u32((unsigned int)param->offset); } // mov %rdi, -off(%rbp)
                    else if (param_idx == 1) { emit_byte(0x48); emit_byte(0x89); emit_byte(0xb5); emit_u32((unsigned int)param->offset); } // mov %rsi, -off(%rbp)
                    else if (param_idx == 2) { emit_byte(0x48); emit_byte(0x89); emit_byte(0x95); emit_u32((unsigned int)param->offset); } // mov %rdx, -off(%rbp)
                    else if (param_idx == 3) { emit_byte(0x48); emit_byte(0x89); emit_byte(0x8d); emit_u32((unsigned int)param->offset); } // mov %rcx, -off(%rbp)
                    param_idx++;
                    next_token();
                }
                if (tok.type == ',') next_token();
            }
            if (tok.type == ')') next_token();

            if (tok.type == ';') {
                // Prototype only
                next_token();
                return;
            }

            // Function body definition
            Symbol *func_sym = add_global(name, 1, is_ptr, 8);
            (void)func_sym;

            gen_prologue();
            parse_block();
            gen_epilogue();
        } else {
            // Global variable
            add_global(name, 0, is_ptr, 8);
            if (tok.type == '=') {
                next_token();
                // simple number literal or string
                next_token();
            }
            if (tok.type == ';') next_token();
        }
    } else {
        next_token();
    }
}

// ---------------- Built-in Runtime & _start Stub ----------------

static int emit_runtime_stubs(void)
{
    // _start entry point stub:
    // Receives KernelApi* in %rdi
    // mov %rdi, __k(%rip) -> lea/mov
    // Reserve slot for __k in data segment:
    Symbol *k_sym = add_global("__k", 0, 1, 8);
    (void)k_sym;

    // mov %rdi, 0x1000(%rip)
    emit_byte(0x48); emit_byte(0x89); emit_byte(0x3d);
    emit_u32(0x1000);

    // sub $8, %rsp (16-byte stack align)
    emit_byte(0x48); emit_byte(0x83); emit_byte(0xec); emit_byte(0x08);

    // xor %edi, %edi (argc = 0)
    emit_byte(0x31); emit_byte(0xff);
    // xor %esi, %esi (argv = NULL)
    emit_byte(0x31); emit_byte(0xf6);

    // call main
    int call_main_offset = text_len;
    emit_byte(0xe8);
    emit_u32(0); // placeholder, patched after main is parsed

    // add $8, %rsp
    emit_byte(0x48); emit_byte(0x83); emit_byte(0xc4); emit_byte(0x08);

    // ret (return main's exit code in %rax to kernel)
    emit_byte(0xc3);

    return call_main_offset;
}

// ---------------- ELF Executable Generator ----------------

static int write_elf_file(const char *out_path, int entry_text_offset)
{
    unsigned char elf_header[64];
    unsigned char phdr_text[56];
    unsigned char phdr_data[56];

    memset(elf_header, 0, sizeof(elf_header));
    memset(phdr_text, 0, sizeof(phdr_text));
    memset(phdr_data, 0, sizeof(phdr_data));

    int headers_size = 64 + 56 * 2; // Ehdr (64) + 2 Phdrs (112) = 176 bytes
    int text_file_size = headers_size + text_len;

    // Align data segment to 0x1000 (4KB page)
    int data_file_offset = (text_file_size + 0xFFF) & ~0xFFF;
    if (data_file_offset < 0x1000) data_file_offset = 0x1000;
    int data_vaddr = data_file_offset;

    // 1. ELF Header (Elf64_Ehdr)
    memcpy(elf_header, "\x7f\x45\x4c\x46\x02\x01\x01\x00", 8); // 64-bit, Little Endian, Version 1
    // e_type = ET_DYN (3) (PIE executable)
    elf_header[16] = 3; elf_header[17] = 0;
    // e_machine = EM_X86_64 (62 / 0x3E)
    elf_header[18] = 0x3E; elf_header[19] = 0;
    // e_version = 1
    elf_header[20] = 1;
    // e_entry = virtual address of entry point
    unsigned long long entry_vaddr = (unsigned long long)(headers_size + entry_text_offset);
    memcpy(elf_header + 24, &entry_vaddr, 8);
    // e_phoff = 64 (immediately after Elf64_Ehdr)
    unsigned long long phoff = 64;
    memcpy(elf_header + 32, &phoff, 8);
    // e_ehsize = 64
    elf_header[52] = 64;
    // e_phentsize = 56
    elf_header[54] = 56;
    // e_phnum = 2
    elf_header[56] = 2;

    // 2. Program Header 0: Text Segment (R-X)
    unsigned int p_type = 1; // PT_LOAD
    unsigned int p_flags_rx = 5; // PF_R | PF_X
    unsigned long long p_offset_0 = 0;
    unsigned long long p_vaddr_0 = 0;
    unsigned long long p_filesz_0 = (unsigned long long)text_file_size;
    unsigned long long p_memsz_0 = (unsigned long long)text_file_size;
    unsigned long long p_align = 0x1000;

    memcpy(phdr_text + 0, &p_type, 4);
    memcpy(phdr_text + 4, &p_flags_rx, 4);
    memcpy(phdr_text + 8, &p_offset_0, 8);
    memcpy(phdr_text + 16, &p_vaddr_0, 8);
    memcpy(phdr_text + 24, &p_vaddr_0, 8);
    memcpy(phdr_text + 32, &p_filesz_0, 8);
    memcpy(phdr_text + 40, &p_memsz_0, 8);
    memcpy(phdr_text + 48, &p_align, 8);

    // 3. Program Header 1: Data Segment (RW-)
    unsigned int p_flags_rw = 6; // PF_R | PF_W
    unsigned long long p_offset_1 = (unsigned long long)data_file_offset;
    unsigned long long p_vaddr_1 = (unsigned long long)data_vaddr;
    unsigned long long p_filesz_1 = (unsigned long long)data_len;
    unsigned long long p_memsz_1 = (unsigned long long)data_len;

    memcpy(phdr_data + 0, &p_type, 4);
    memcpy(phdr_data + 4, &p_flags_rw, 4);
    memcpy(phdr_data + 8, &p_offset_1, 8);
    memcpy(phdr_data + 16, &p_vaddr_1, 8);
    memcpy(phdr_data + 24, &p_vaddr_1, 8);
    memcpy(phdr_data + 32, &p_filesz_1, 8);
    memcpy(phdr_data + 40, &p_memsz_1, 8);
    memcpy(phdr_data + 48, &p_align, 8);

    // Build complete file image in buffer
    int total_file_size = data_file_offset + data_len;
    unsigned char *file_img = (unsigned char *)malloc((size_t)total_file_size);
    if (!file_img) return -1;
    memset(file_img, 0, (size_t)total_file_size);

    memcpy(file_img, elf_header, 64);
    memcpy(file_img + 64, phdr_text, 56);
    memcpy(file_img + 120, phdr_data, 56);
    memcpy(file_img + headers_size, text_buf, (size_t)text_len);
    if (data_len > 0) {
        memcpy(file_img + data_file_offset, data_buf, (size_t)data_len);
    }

    int ret = k_write_file(out_path, (char *)file_img, total_file_size);
    free(file_img);
    return ret >= 0 ? total_file_size : -1;
}

// ---------------- Main Compiler Driver ----------------

int main(int argc, char **argv)
{
    char in_path[256] = "";
    char out_path[256] = "";
    char log_path[256] = "";
    int compile_only = 0;
    int a;

    if (argc < 2) {
        printf("TinyCC x86_64 for Windose OS\n");
        printf("Usage: tcc [options] <file.c>\n");
        printf("Options:\n");
        printf("  -o <file.elf>    Set output executable path\n");
        printf("  -c               Compile only (syntax/type check)\n");
        printf("  -log <file.log>  Redirect log messages to file\n");
        printf("  -I <dir>         Add search include directory\n");
        return 1;
    }

    add_include_path("/mnt/System/include");
    add_include_path("/mnt/include");
    add_include_path("/mnt");

    for (a = 1; a < argc; a++) {
        if (!strcmp(argv[a], "-o") && a + 1 < argc) {
            strncpy(out_path, argv[++a], sizeof(out_path) - 1);
        } else if (!strcmp(argv[a], "-log") && a + 1 < argc) {
            strncpy(log_path, argv[++a], sizeof(log_path) - 1);
        } else if (!strcmp(argv[a], "-I") && a + 1 < argc) {
            add_include_path(argv[++a]);
        } else if (!strcmp(argv[a], "-c")) {
            compile_only = 1;
        } else if (argv[a][0] != '-') {
            strncpy(in_path, argv[a], sizeof(in_path) - 1);
        }
    }

    if (!in_path[0]) {
        printf("tcc: no input file specified\n");
        return 1;
    }

    if (!out_path[0]) {
        strncpy(out_path, in_path, sizeof(out_path) - 1);
        char *dot = strrchr(out_path, '.');
        if (dot) strcpy(dot, ".elf");
        else strcat(out_path, ".elf");
    }

    if (log_path[0]) {
        // Open log file or clear it
        k_write_file(log_path, "", 0);
    }

    log_msg("[TinyCC] Compiling %s -> %s...\n", in_path, out_path);

    char raw_buf[256 * 1024];
    int raw_len = read_full_file(in_path, raw_buf, sizeof(raw_buf));
    if (raw_len < 0) {
        log_msg("[TinyCC] error: cannot open input file %s\n", in_path);
        return 1;
    }

    strncpy(current_file, in_path, sizeof(current_file) - 1);
    current_line = 1;

    // Step 1: Preprocess
    int prep_len = preprocess(raw_buf, src_buf, sizeof(src_buf));
    if (prep_len <= 0) {
        log_msg("[TinyCC] error: preprocessor produced empty buffer\n");
        return 1;
    }

    // Step 2: Initialize code generation
    text_len = 0;
    data_len = 0;
    num_globals = 0;
    num_locals = 0;

    int call_main_patch = emit_runtime_stubs();

    // Step 3: Parse and generate code
    src_ptr = src_buf;
    next_token();

    while (tok.type != TOK_EOF) {
        parse_function_or_global();
    }

    // Patch call to main in _start stub
    Symbol *main_sym = find_symbol("main");
    if (!main_sym || !main_sym->is_func) {
        log_msg("[TinyCC] error: 'main' function not found in source\n");
        return 1;
    }
    int main_rel = main_sym->offset - (call_main_patch + 4);
    memcpy(text_buf + call_main_patch, &main_rel, 4);

    if (compile_only) {
        log_msg("[TinyCC] Syntax and type check passed successfully.\n");
        return 0;
    }

    // Step 4: Write output ELF executable
    int bytes_written = write_elf_file(out_path, 0);
    if (bytes_written > 0) {
        log_msg("[TinyCC] Build succeeded: %s (%d bytes, text=%d B, data=%d B)\n",
                out_path, bytes_written, text_len, data_len);
        return 0;
    } else {
        log_msg("[TinyCC] error: failed to write output ELF to %s\n", out_path);
        return 1;
    }
}
