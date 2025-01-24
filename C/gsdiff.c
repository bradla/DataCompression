/************************** Start of GSDIFF.C *************************
 *
 * This is the GSDIFF program, which displays the differences between
 * two grey scale files on the IBM VGA adaptor.  It assumes that the grey scale
 * values run from 0 to 255, and scales them down to a range of 0 to 63, so
 * they will be displayed properly on the VGA.
 *
 * This program writes directly to video memory, which should work properly
 * on most VGA adaptors.  In the event that it doesn't, the constant
 * USE_BIOS can be turned on, and the code will use BIOS calls to write
 * pixels instead.  This will be somewhat slower, but should work on
 * every VGA adaptor.
 *
 * While this program is writing out to the display, it is also keeping a
 * running total of the error differences.  When the program is
 * complete, it prints out the RMS error.  If the -B switch is turned
 * on, the program operates in batch mode, and doesn't display the
 * differences.  It just computes and prints the rms error value.
 *
 * Note that the use of far pointers means this program should probably
 * be compiled without using the strict ANSI option of your compiler.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <dos.h>
#include <conio.h>
#include <math.h>

main( int argc, char *argv[] )
{
    union REGS rin;
    union REGS rout;
    int i;
    FILE *file1;
    FILE *file2;
    int diff;
    int c1;
    int c2;
    char far *video;
    double error;
    int batch;

    if ( argc < 3 ) {
        printf( "Usage: gsdiff file1 file2 [-B]\n\n" );
        exit( 1 );
    }
    file1 = fopen( argv[ 1 ], "rb" );
    file2 = fopen( argv[ 2 ], "rb" );
    if ( file1 == NULL || file2 == NULL ) {
        printf( "Could not open file!\n" );
        exit( 1 );
    }
    batch = 0;
    if ( argc > 3 )
        if ( strcmp( argv[ 3 ], "-b" ) == 0 ||
             strcmp( argv[ 3 ], "-B" ) == 0 )
            batch = 1;
    if ( !batch ) {
        rin.h.ah = 0;
        rin.h.al = 0x13;
        int86( 0x10, &rin, &rout );
        rin.h.ah = 0x10;
        rin.h.al = 0x10;
        for ( i = 0 ; i < 64 ; i++ ) {
            rin.h.dh = (unsigned char) i;
            rin.h.ch = (unsigned char) i;
            rin.h.cl = (unsigned char) i;
            rin.x.bx = i;
            int86( 0x10, &rin, &rout );
        }
        rin.h.ah = 0x10;
        rin.h.al = 0x1b;
        rin.x.cx = 256;
        rin.x.bx = 0;
        int86( 0x10, &rin, &rout );
    }
    error = 0.0;
    video = (char far *) 0xA0000000L;
    rin.h.ah = 0x0c;
    rin.h.bh = 0;
    for ( rin.x.dx = 0 ; rin.x.dx < 200 ; rin.x.dx++ ) {
        for ( rin.x.cx = 0 ; rin.x.cx < 320 ; rin.x.cx++ ) {
            c1 = getc( file1 );
            c2 = getc( file2 );
            diff = c1 - c2;
            error += diff*diff;
            if ( diff < 0 )
                diff *= -1;
            if ( diff > 63 )
               diff = 63;
            if ( !batch ) {
#ifdef USE_BIOS
                rin.h.al = (unsigned char) diff;
                int86( 0x10, &rin, &rout );
#else
                *video++ = (unsigned char) diff;
#endif
            }
        }
    }
    fclose( file1 );
    fclose( file2 );
    if ( !batch ) {
        getch();
        rin.h.ah = 0;
        rin.h.al = 3;
        int86( 0x10, &rin, &rout );
    }
    error /= 320.0 * 200.0;
    printf( "RMS error between %s and %s is %lf\n",
            argv[ 1 ], argv[ 2 ], sqrt( error ) );
    return 0;
}

/************************** End of GSDIFF.C *************************/

