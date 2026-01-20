#define LINUX 1
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <dirent.h>

#ifdef __GNUC__

#include <sys/stat.h>
#include <sys/types.h>
#define FILE_INFO                 struct dirent
#define FIND_FIRST( dir, info )   ( ( info = readdir( dir ) ) != NULL )
#define FILE_IS_DIR( path, info ) ( stat( path, &st ) == 0 && S_ISDIR( st.st_mode ) )
#define FILE_NAME( info )         ( ( info )->d_name )

#else

#error "Unsupported compiler."

#endif

int total_files;
int total_passed;
int total_failed;
char *compress_command;
char *expand_command;
FILE *input;
FILE *output;
FILE *compressed;
FILE *log_file;

void churn_files( const char *path );
int file_is_already_compressed( const char *name );
void close_all_the_files( void );
int compress( const char *file_name );
void usage_exit( void );

void main( int argc, char *argv[] )
{
    time_t start_time;
    time_t stop_time;
    char root_dir[ 1024 ];

    if ( argc != 4 )
        usage_exit();
    strcpy( root_dir, argv[ 1 ] );
    if ( root_dir[ strlen( root_dir ) - 1 ] != '/' )
       strcat( root_dir, "/" );
    compress_command = argv[ 2 ];
    expand_command = argv[ 3 ];
    setbuf( stdout, NULL );
    setbuf( stderr, NULL );
    total_files = 0;
    total_passed = 0;
    total_failed = 0;
    log_file = fopen( "CHURN.LOG", "w" );
    if ( log_file == NULL ) {
        printf( "Couldn't open the log file!\n" );
        exit( 1 );
    }
    fprintf( log_file, "                                          "
            "Original   Packed\n" );
    fprintf( log_file, "            File Name                     "
            "  Size      Size   Ratio  Result\n" );
    fprintf( log_file, "-------------------------------------     "
            "--------  --------  ----  ------\n" );
    time( &start_time );
    churn_files( root_dir );
    time( &stop_time );
    fprintf( log_file, "\nTotal elapsed time: %f seconds\n",
             difftime( stop_time, start_time ) );
    fprintf( log_file, "Total files:   %d\n", total_files );
    fprintf( log_file, "Total passed:  %d\n", total_passed );
    fprintf( log_file, "Total failed:  %d\n", total_failed );
}


void churn_files( const char *path )
{
    DIR *dir;
    FILE_INFO *file_info;
    struct stat st;
    char full_name[ 1024 ];

    dir = opendir( path );
    if ( dir == NULL ) {
        perror( "Could not open directory" );
        exit( 1 );
    }

    while ( FIND_FIRST( dir, file_info ) ) {
        if ( strcmp( FILE_NAME( file_info ), "." ) == 0 || strcmp( FILE_NAME( file_info ), ".." ) == 0 )
            continue;

        snprintf( full_name, sizeof(full_name), "%s/%s", path, FILE_NAME( file_info ) );
        if ( FILE_IS_DIR( full_name, file_info ) ) {
            churn_files( full_name );
        } else {
            if ( !file_is_already_compressed( full_name ) ) {
                fprintf( stderr, "Testing %s\n", full_name );
                if ( !compress( full_name ) )
                    fprintf( stderr, "Comparison failed!\n" );
            }
        }
    }

    closedir( dir );
}


int file_is_already_compressed( const char *name )
{
    const char *extension;
    static const char *matches[]={ "zip", "gz", "bz2", "xz", "tar", "rar", NULL };
    int i;

    extension = strrchr( name, '.' );
    if ( extension == NULL || ++extension == NULL )
        return( 0 );
    i = 0;
    while ( matches[ i ] != NULL )
        if ( strcmp( extension, matches[ i++ ] ) == 0 )
            return( 1 );
    return( 0 );
}


int compress( const char *file_name )
{
    long new_size;
    long old_size;
    int c;
    char command[ 132 ];

    printf( "%s\n", file_name );
    fprintf( log_file, "%-40s ", file_name );
    snprintf( command, sizeof(command), compress_command, file_name );
    system( command );
    snprintf( command, sizeof(command), expand_command, file_name );
    system( command );

    input = fopen( file_name, "rb" );
    output = fopen( "TEST.OUT", "rb" );
    compressed = fopen( "TEST.CMP", "rb" );

    total_files++;
    if ( input == NULL || output == NULL || compressed == NULL ) {
        total_failed++;
        close_all_the_files();
        fprintf( log_file, "Failed, couldn't open file!\n" );
        return( 0 );
    }

    fseek( input, 0L, SEEK_END );
    old_size = ftell( input );
    fseek( input, 0L, SEEK_SET );
    fseek( compressed, 0L, SEEK_END );
    new_size = ftell( compressed );

    fprintf( log_file, " %8ld  %8ld ", old_size, new_size );
    if ( old_size == 0L )
        old_size = 1L;
    fprintf( log_file, "%4ld%%  ",
            100L - ( ( 100L * new_size ) / old_size ) );
    do {
        c = getc( input );
        if ( getc( output ) != c ) {
            fprintf( log_file, "Failed\n" );
            total_failed++;
            close_all_the_files();
            return( 0 );
        }
    }
    while ( c != EOF );
    fprintf( log_file, "Passed\n" );
    close_all_the_files();
    total_passed++;
    return( 1 );
}

void close_all_the_files()
{
    if ( input != NULL )
        fclose( input );
    if ( output != NULL )
        fclose( output );
    if ( compressed != NULL )
        fclose( compressed );
}


void usage_exit( void )
{
    const char *usage = "CHURN 1.0. Usage:  CHURN root-dir \"compress "
                  "command\" \"expand command\n"
                  "\n"
                  "CHURN is used to test compression programs.  "
                  "It does this by compressing and\n"
                  "then expanding all of the files in and under "
                  "the specified root dir.\n"
                  "\n"
                  "For each file it finds, CHURN first executes "
                  "the compress command to create a\n"
                  "compressed file called TEST.CMP.  It then "
                  "executes the expand command to\n"
                  "create a file called TEST.OUT. CHURN then "
                  "compares the two files to make sure\n"
                  "the compression cycle worked properly.\n"
                  "\n"
                  "The file name to be compressed will be "
                  "inserted into the compress command\n"
                  "using sprintf, with any %%s argument being "
                  "substituted with the name of the\n"
                  "file being compressed.  Note that the "
                  "compress and expand commands should be\n"
                  "enclosed in double quotes so that multiple "
                  "words can be included in the\n"
                  "commands.\n"
                  "\n"
                  "Note that you may have to double the %% "
                  "character on your command line to get\n"
                  "around argument substitution under some "
                  "command processors. Finally, note that\n"
                  "CHURN executes the compression program "
                  "using a system() function call, so\n"
                  "batch files can be used to execute complex "
                  "compression sequences.\n"
                  "\n"
                  "Example:  CHURN / \"gzip -c %%s > TEST.CMP\""
                  " \"gzip -d -c TEST.CMP > TEST.OUT\"";

    puts( usage );
    exit( 1 );
}