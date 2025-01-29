arith: Arith.cs
	csc Main-c.cs Bitio.cs Arith.cs -out:Arith-c.exe
	csc Main-e.cs Bitio.cs Arith.cs -out:Arith-e.exe
	
arith1e: Arith1e.cs
	csc Main-c.cs Bitio.cs Arith1e.cs -out:Arith1e-c.exe
	csc Main-e.cs Bitio.cs Arith1e.cs -out:Arith1e-e.exe

arith1: Arith1.cs
	csc Main-c.cs Bitio.cs Arith1.cs -out:Arith1-c.exe
	csc Main-e.cs Bitio.cs Arith1.cs -out:Arith1-e.exe
	
huff: Huff.cs
	csc Main-c.cs Bitio.cs Huff.cs -out:Huff-c.exe
	csc Main-e.cs Bitio.cs Huff.cs -out:Huff-e.exe
	
lzw12: Lzw12.cs
	csc Main-c.cs Bitio.cs Lzw12.cs -out:Lzw12-c.exe
	csc Main-e.cs Bitio.cs Lzw12.cs -out:Lzw12-e.exe

lzw15v: Lzw15v.cs
	csc Main-c.cs Bitio.cs Lzw15v.cs -out:Lzw15v-c.exe
	csc Main-e.cs Bitio.cs Lzw15v.cs -out:Lzw15v-e.exe

churn: Churn.cs
	csc Churn.cs -out:Churn.exe

#all: arith arith-n arith1 arith1e ahuff huff dct lzss lzw12 lzw15v silence compand carman churn
all: arith arith1e arith1 huff lzw12 lzw15v churn 
