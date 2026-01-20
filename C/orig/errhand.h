/************************* Start of ERRHAND.H ************************/

#ifndef _ERRHAND_H
#define _ERRHAND_H

#ifdef __STDC__

void fatal_error( char *fmt, ... );

void print_profiles();
void profile_end();
void profile_start(const char *func_name);
void get_cpu_times(double *user_time, double *system_time);

#else   /* __STDC__ */

void fatal_error();

#endif  /* __STDC__ */
// Macro to automatically profile functions
#define PROFILE_FUNCTION() \
    profile_start(__func__); \
    for (int _done = 0; !_done; _done = 1, profile_end())

#endif  /* _ERRHAND_H */

/************************** End of ERRHAND.H *************************/
