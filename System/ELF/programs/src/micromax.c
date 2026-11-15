#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

// micromax - Terminal 8x8 Chess Game & Engine for Windose
// Usage: enter moves like 'e2e4', 'g8f6', or 'quit'.

static int board[128];

static void print_board(void)
{
    printf("\n    a b c d e f g h\n");
    printf("  +-----------------+\n");
    for (int r = 7; r >= 0; r--) {
        printf("%d | ", r + 1);
        for (int f = 0; f < 8; f++) {
            int sq = (r << 4) + f;
            int p = board[sq];
            char c = '.';
            if (p) {
                int is_black = (p & 16) != 0;
                int piece = (p & 7);
                const char *w_names = ".PNBRQK";
                const char *b_names = ".pnbrqk";
                c = is_black ? b_names[piece] : w_names[piece];
            }
            printf("%c ", c);
        }
        printf("| %d\n", r + 1);
    }
    printf("  +-----------------+\n");
    printf("    a b c d e f g h\n\n");
}

static void init_game(void)
{
    for (int i = 0; i < 128; i++) board[i] = 0;

    // Black pieces (piece | 16)
    board[0x70] = board[0x77] = 2 | 16; // Rooks
    board[0x71] = board[0x76] = 3 | 16; // Knights
    board[0x72] = board[0x75] = 4 | 16; // Bishops
    board[0x73] = 5 | 16;               // Queen
    board[0x74] = 6 | 16;               // King
    for (int i = 0; i < 8; i++) board[0x60 + i] = 1 | 16; // Pawns

    // White pieces (piece)
    board[0x00] = board[0x07] = 2;      // Rooks
    board[0x01] = board[0x06] = 3;      // Knights
    board[0x02] = board[0x05] = 4;      // Bishops
    board[0x03] = 5;                    // Queen
    board[0x04] = 6;                    // King
    for (int i = 0; i < 8; i++) board[0x10 + i] = 1;      // Pawns
}

static int is_on_board(int sq)
{
    return (sq & 0x88) == 0;
}

// Generate legal candidate moves for Black and select the best one
static int find_black_move(int *out_from, int *out_to)
{
    int best_score = -99999;
    int best_from = -1;
    int best_to = -1;

    static const int knight_offsets[] = { -33, -31, -18, -14, 14, 18, 31, 33 };
    static const int bishop_offsets[] = { -17, -15, 15, 17 };
    static const int rook_offsets[]   = { -16, -1, 1, 16 };
    static const int king_offsets[]   = { -17, -16, -15, -1, 1, 15, 16, 17 };
    static const int piece_values[]   = { 0, 100, 300, 310, 500, 900, 9999 };

    for (int from = 0; from < 128; from++) {
        if (!is_on_board(from)) continue;
        int p = board[from];
        if (!p || !(p & 16)) continue; // Must be Black

        int piece_type = p & 7;

        // 1. Black Pawn
        if (piece_type == 1) {
            // Forward 1
            int f1 = from - 16;
            if (is_on_board(f1) && board[f1] == 0) {
                int score = 10 + (7 - (f1 >> 4));
                if (score > best_score) { best_score = score; best_from = from; best_to = f1; }

                // Forward 2 from row 7
                if ((from >> 4) == 6) {
                    int f2 = from - 32;
                    if (is_on_board(f2) && board[f2] == 0) {
                        int s2 = 25;
                        if (s2 > best_score) { best_score = s2; best_from = from; best_to = f2; }
                    }
                }
            }
            // Diagonal captures
            int caps[2] = { from - 17, from - 15 };
            for (int i = 0; i < 2; i++) {
                int c_sq = caps[i];
                if (is_on_board(c_sq) && board[c_sq] && !(board[c_sq] & 16)) {
                    int target_val = piece_values[board[c_sq] & 7];
                    int score = target_val * 10 + 50;
                    if (score > best_score) { best_score = score; best_from = from; best_to = c_sq; }
                }
            }
        }
        // 2. Black Knight
        else if (piece_type == 3) {
            for (int i = 0; i < 8; i++) {
                int to = from + knight_offsets[i];
                if (!is_on_board(to)) continue;
                int dest_p = board[to];
                if (dest_p && (dest_p & 16)) continue; // Can't take own piece
                int score = dest_p ? (piece_values[dest_p & 7] * 10 + 20) : 5;
                if (score > best_score) { best_score = score; best_from = from; best_to = to; }
            }
        }
        // 3. Black Bishop / Rook / Queen
        else if (piece_type == 4 || piece_type == 2 || piece_type == 5) {
            int num_dirs = (piece_type == 5) ? 8 : 4;
            const int *dirs = (piece_type == 2) ? rook_offsets : ((piece_type == 4) ? bishop_offsets : king_offsets);
            for (int d = 0; d < num_dirs; d++) {
                int step = dirs[d];
                int to = from + step;
                while (is_on_board(to)) {
                    int dest_p = board[to];
                    if (dest_p) {
                        if (!(dest_p & 16)) { // Capture White
                            int score = piece_values[dest_p & 7] * 10 + 15;
                            if (score > best_score) { best_score = score; best_from = from; best_to = to; }
                        }
                        break;
                    }
                    int score = 3;
                    if (score > best_score) { best_score = score; best_from = from; best_to = to; }
                    to += step;
                }
            }
        }
        // 4. Black King
        else if (piece_type == 6) {
            for (int i = 0; i < 8; i++) {
                int to = from + king_offsets[i];
                if (!is_on_board(to)) continue;
                int dest_p = board[to];
                if (dest_p && (dest_p & 16)) continue;
                int score = dest_p ? (piece_values[dest_p & 7] * 10 + 10) : 1;
                if (score > best_score) { best_score = score; best_from = from; best_to = to; }
            }
        }
    }

    if (best_from != -1 && best_to != -1) {
        *out_from = best_from;
        *out_to = best_to;
        return 1;
    }
    return 0;
}

int main(int argc, char **argv)
{
    (void)argc;
    (void)argv;

    printf("=== Windose MicroMax Chess ===\n");
    printf("Type your move (e.g. 'e2e4', 'd2d4', 'g1f3') or 'quit'.\n");

    init_game();
    print_board();

    char line[128];
    while (1) {
        printf("White move: ");
        fflush(stdout);

        if (!fgets(line, sizeof(line), stdin)) {
            break;
        }

        // Clean newline / spaces
        int len = (int)strlen(line);
        while (len > 0 && (line[len - 1] == '\n' || line[len - 1] == '\r' || line[len - 1] == ' ')) {
            line[--len] = '\0';
        }

        if (len == 0) continue;
        if (strcmp(line, "quit") == 0 || strcmp(line, "exit") == 0 || strcmp(line, "q") == 0) {
            break;
        }

        if (len < 4) {
            printf("Invalid move format! Use coordinate notation like e2e4.\n");
            continue;
        }

        int from_f = line[0] - 'a';
        int from_r = line[1] - '1';
        int to_f   = line[2] - 'a';
        int to_r   = line[3] - '1';

        if (from_f < 0 || from_f > 7 || from_r < 0 || from_r > 7 ||
            to_f < 0 || to_f > 7 || to_r < 0 || to_r > 7) {
            printf("Invalid coordinates! File must be a-h, rank 1-8.\n");
            continue;
        }

        int from_sq = (from_r << 4) + from_f;
        int to_sq   = (to_r << 4) + to_f;

        if (!board[from_sq] || (board[from_sq] & 16) != 0) {
            printf("No White piece at %c%d!\n", 'a' + from_f, from_r + 1);
            continue;
        }

        if (board[to_sq] && !(board[to_sq] & 16)) {
            printf("Cannot capture your own White piece at %c%d!\n", 'a' + to_f, to_r + 1);
            continue;
        }

        // Execute White move
        int captured = board[to_sq];
        board[to_sq] = board[from_sq];
        board[from_sq] = 0;

        // Pawn promotion to Queen on row 8
        if ((board[to_sq] & 7) == 1 && to_r == 7) {
            board[to_sq] = 5; // Queen
            printf("Pawn promoted to Queen!\n");
        }

        if (captured) {
            printf("Captured Black %c!\n", ".pnbrqk"[captured & 7]);
        }

        print_board();

        // Black Turn
        printf("Black thinking...\n");
        int b_from = 0, b_to = 0;
        if (find_black_move(&b_from, &b_to)) {
            int b_cap = board[b_to];
            board[b_to] = board[b_from];
            board[b_from] = 0;

            // Black pawn promotion on row 1
            if ((board[b_to] & 7) == 1 && (b_to >> 4) == 0) {
                board[b_to] = 5 | 16;
            }

            printf("Black plays: %c%d%c%d\n",
                   'a' + (b_from & 7), (b_from >> 4) + 1,
                   'a' + (b_to & 7), (b_to >> 4) + 1);

            if (b_cap) {
                printf("Black captured your %c!\n", ".PNBRQK"[b_cap & 7]);
                if ((b_cap & 7) == 6) {
                    print_board();
                    printf("Black captured your King! Checkmate! Game over.\n");
                    break;
                }
            }

            print_board();
        } else {
            printf("Black has no legal moves! You win!\n");
            break;
        }
    }

    printf("Thanks for playing Windose Chess!\n");
    return 0;
}
