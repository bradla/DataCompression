lzw12: lzw12.cs
	csc Main-c.cs Bitio.cs lzw12.cs -out:lzw12-c.exe
	csc Main-e.cs Bitio.cs lzw12.cs -out:lzw12-e.exe

lzw15v: lzw15v.cs
	csc Main-c.cs Bitio.cs lzw15v.cs -out:lzw15v-c.exe
	csc Main-e.cs Bitio.cs lzw15v.cs -out:lzw15v-e.exe

churn: Churn.cs
	csc Churn.cs -out:Churn.exe

#all: arith arith-n arith1 arith1e ahuff huff dct lzss lzw12 lzw15v silence compand carman churn
all: lzw12 lzw15v churn 
