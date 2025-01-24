/************************** Start of GS.C *************************
 *
 * This is the GS program, which display grey scale files on the
 * IBM VGA adaptor.  It assumes that the grey scale values run from
 * 0 to 255, and scales them down to a range of 0 to 63, so they will
 * be displayed properly on the VGA.
 *
 * This program can be called with a list of files, and will display them
 * in consecutive order, which is useful for trying to measure visual
 * differences in compressed files.
 *
 * This program writes directly to video memory, which should work properly
 * on most VGA adaptors.  In the event that it doesn't, the constant
 * USE_BIOS can be turned on, and the code will use BIOS calls to write
 * pixels instead.  This will be somewhat slower, but should work on
 * every VGA adaptor.
 *
 * Note that the use of far pointers means this program should probably
 * be compiled without using the strict ANSI option of your compiler.
 */

#include <stdio.h>
#include <stdlib.h>
#include <dos.h>
#include <conio.h>

main( int argc, char *argv[] )
{
    union REGS rin;
    union REGS rout;
    int i;
    FILE *file;
    char far *video;

    if ( argc < 2 ) {
        printf( "Usage:  gs file\n\n" );
        exit( 1 );
    }
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

    argv++;
    while ( --argc > 0 ) {
        file = fopen( *argv++, "rb" );
        if ( file == NULL ) {
            putc( 7, stdout );
            break;
        }
        video = (char far *) 0xA0000000L;
        rin.h.ah = 0x0c;
        rin.h.bh = 0;
        for ( rin.x.dx = 0 ; rin.x.dx < 200 ; rin.x.dx++ ) {
           for ( rin.x.cx = 0 ; rin.x.cx < 320 ; rin.x.cx++ ) {
#ifdef USE_BIOS
               rin.h.al = (unsigned char) ( getc( file ) >> 2 );
               int86( 0x10, &rin, &rout );
#else
               *video++ = (char) ( getc( file ) >> 2 );
#endif
           }
        }
        fclose( file );
        getch();
    }
    rin.h.ah = 0;
    rin.h.al = 3;
    int86( 0x10, &rin, &rout );
    return 0;
}

/************************** End of GS.C *************************/

