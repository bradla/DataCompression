LIBS = -lm

arith1e: arith1e.c
	gcc -Wall main-c.c bitio.c errhand.c arith1e.c -o arith1e-c
	gcc -Wall main-e.c bitio.c errhand.c arith1e.c -o arith1e-e

arith1: arith1.c
	gcc -Wall main-c.c bitio.c errhand.c arith1.c -o arith1-c
	gcc -Wall main-e.c bitio.c errhand.c arith1.c -o arith1-e

arith: arith.c
	gcc -Wall main-c.c bitio.c errhand.c arith.c -o arith-c
	gcc -Wall main-e.c bitio.c errhand.c arith.c -o arith-e

arith-n: arith-n.c
	gcc -Wall main-c.c bitio.c errhand.c arith-n.c -o arith-n-c
	gcc -Wall main-e.c bitio.c errhand.c arith-n.c -o arith-n-e

ahuff: ahuff.c
	gcc -Wall main-c.c bitio.c errhand.c ahuff.c -o ahuff-c
	gcc -Wall main-e.c bitio.c errhand.c ahuff.c -o ahuff-e

huff: huff.c
	gcc -Wall main-c.c bitio.c errhand.c huff.c -o huff-c
	gcc -Wall main-e.c bitio.c errhand.c huff.c -o huff-e

compand: compand.c 
	gcc -Wall main-c.c bitio.c errhand.c compand.c -o compand-c $(LIBS)
	gcc -Wall main-e.c bitio.c errhand.c compand.c -o compand-e $(LIBS)

dct: dct.c
	gcc -Wall main-c.c bitio.c errhand.c dct.c -o dct-c $(LIBS)
	gcc -Wall main-e.c bitio.c errhand.c dct.c -o dct-e $(LIBS)

lzss: lzss.c
	gcc -Wall main-c.c bitio.c errhand.c lzss.c -o lzss-c
	gcc -Wall main-e.c bitio.c errhand.c lzss.c -o lzss-e

lzw12: lzw12.c
	gcc -Wall main-c.c bitio.c errhand.c lzw12.c -o lzw12-c
	gcc -Wall main-e.c bitio.c errhand.c lzw12.c -o lzw12-e

lzw15v: lzw15v.c
	gcc -Wall main-c.c bitio.c errhand.c lzw15v.c -o lzw15v-c
	gcc -Wall main-e.c bitio.c errhand.c lzw15v.c -o lzw15v-e

silence: silence.c
	gcc -Wall main-c.c bitio.c errhand.c silence.c -o silence-c
	gcc -Wall main-e.c bitio.c errhand.c silence.c -o silence-e

carman: carman.c
	gcc -Wall carman.c -o carman 

churn: churn.c
	gcc -Wall churn.c -o churn

#linux only
gs: gs.c
	gcc -Wall gs.c -o gs -lSDL2

all: arith arith-n arith1 arith1e ahuff huff dct lzss lzw12 lzw15v silence compand carman churn
# Clean up build artifacts
clean:
ifdef OS
	del /Q *.exe
else
	rm -f ahuff-c ahuff-e arith1-c arith1-e arith1e-c arith1e-e arith-n-c arith-n-e carman churn compand-c compand-e dct-c dct-e gs huff-c huff-e lzss-c lzss-e lzw12-c lzw12-e lzw15v-c lzw15v-e silence-c silence-e
endif
