#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include "bitio.h"
#include "errhand.h"
#include "main.h"

char *CompressionName = "Silence compression";
char *Usage = "infile outfile\n";

#define SILENCE_LIMIT   4
#define START_THRESHOLD 5
#define STOP_THRESHOLD  2
#define SILENCE_CODE    0xff
#define IS_SILENCE( c ) ( (c) > ( 0x7f - SILENCE_LIMIT ) && \
                          (c) < ( 0x80 + SILENCE_LIMIT ) )

#define BUFFER_SIZE 8
#define BUFFER_MASK 7

int silence_run( int buffer[], int index );
int end_of_silence( int buffer[], int index );

void CompressFile ( FILE *input,  BIT_FILE *output,  int argc,  char *argv[] )
{
    int look_ahead[ BUFFER_SIZE ];
    int index;
    int i;
    int run_length;

    for ( i = 0 ; i < BUFFER_SIZE ; i++ )
        look_ahead[ i ] = getc( input );
    index = 0;
    for ( ; ; ) {
        if ( look_ahead[ index ] == EOF )
            break;
        if ( silence_run( look_ahead, index ) ) {
            run_length = 0;
            do {
                look_ahead[ index++ ] = getc( input );
                index &= BUFFER_MASK;
                if ( ++run_length == 255 ) {
                    putc( SILENCE_CODE, output->file );
                    putc( 255, output->file );
                    run_length = 0;
                }
            } while ( !end_of_silence( look_ahead, index ) );
            if ( run_length > 0 ) {
                putc( SILENCE_CODE, output->file );
                putc( run_length, output->file );
            }
        }
        if ( look_ahead[ index ] == SILENCE_CODE )
            look_ahead[ index ]--;
        putc( look_ahead[ index ], output->file );
        look_ahead[ index++ ] = getc( input );
        index &= BUFFER_MASK;
    }
    while ( argc-- > 0 )
        printf( "Unused argument: %s\n", *argv++ );
}

void ExpandFile ( BIT_FILE *input,  FILE *output,  int argc,  char *argv[] )
{
    int c;
    int run_count;

    while ( ( c = getc( input->file ) ) != EOF ) {
        if ( c == SILENCE_CODE ) {
            run_count = getc( input->file );
            while ( run_count-- > 0 )
                putc( 0x80, output );
        } else
            putc( c, output );
    }
    while ( argc-- > 0 )
        printf( "Unused argument: %s\n", *argv++ );
}


int silence_run ( int buffer[],  int index )
{
    int i;

    for ( i = 0 ; i < START_THRESHOLD ; i++ )
        if ( !IS_SILENCE( buffer[ ( index + i ) & BUFFER_MASK ] ) )
            return( 0 );
    return( 1 );
}


int end_of_silence ( int buffer[],  int index )
{
    int i;

    for ( i = 0 ; i < STOP_THRESHOLD ; i++ )
        if ( IS_SILENCE( buffer[ ( index + i ) & BUFFER_MASK ] ) )
            return( 0 );
    return( 1 );
}
