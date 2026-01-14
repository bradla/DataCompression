DOTNET = dotnet
ARITHC_PROJ = csfiles/Arith-c.csproj
ARITHE_PROJ = csfiles/Arith-e.csproj
ARITH1C_PROJ = csfiles/Arith1-c.csproj
ARITH1E_PROJ = csfiles/Arith1-e.csproj
ARITH1eC_PROJ = csfiles/Arith1e-c.csproj
ARITH1eE_PROJ = csfiles/Arith1e-e.csproj

COMPANDC_PROJ = csfiles/Compand-c.csproj
COMPANDE_PROJ = csfiles/Compand-e.csproj
SILENCEC_PROJ = csfiles/Silence-c.csproj
SILENCEE_PROJ = csfiles/Silence-e.csproj

HUFFC_PROJ = csfiles/Huff-c.csproj
HUFFE_PROJ = csfiles/Huff-e.csproj
AHUFFC_PROJ = csfiles/AHuff-c.csproj
AHUFFE_PROJ = csfiles/AHuff-e.csproj

LZW12C_PROJ = csfiles/Lzw12-c.csproj
LZW12E_PROJ = csfiles/Lzw12-e.csproj
LZW15VC_PROJ = csfiles/Lzw15v-c.csproj
LZW15VE_PROJ = csfiles/Lzw15v-e.csproj
LZSSC_PROJ = csfiles/Lzss-c.csproj
LZSSE_PROJ = csfiles/Lzss-e.csproj

CARMAN_PROJ = csfiles/Carman.csproj
CHURN_PROJ = csfiles/Churn.csproj

DCTC_PROJ = csfiles/Dct-c.csproj
DCTE_PROJ = csfiles/Dct-e.csproj

GS_PROJ = csfiles/Gs.csproj

# Common output directories (moved out of csfiles/)
BIN_DIR = build/bin
OBJ_DIR = build/obj

# -------------------------
# Default target
# -------------------------
all: Carman Churn Arith Arith1 Arith1e Compand Silence Lzw15v Lzw12 Lzss Dct Gs Huff Ahuff

# -------------------------
# Build Targets
# -------------------------
Compand:
	$(DOTNET) build $(COMPANDC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(COMPANDE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

Silence:
	$(DOTNET) build $(SILENCEC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(SILENCEE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

Arith:
	$(DOTNET) build $(ARITHC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(ARITHE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Arith1:
	$(DOTNET) build $(ARITH1C_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(ARITH1E_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Arith1e:
	$(DOTNET) build $(ARITH1eC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(ARITH1eE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Huff:
	$(DOTNET) build $(HUFFC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(HUFFE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

AHuff:
	$(DOTNET) build $(AHUFFC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(AHUFFE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

Lzw15v:
	$(DOTNET) build $(LZW15VC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(LZW15VE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Lzw12:
	$(DOTNET) build $(LZW12C_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(LZW12E_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Lzss:
	$(DOTNET) publish $(LZSSC_PROJ) -c Release --self-contained true -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/ -o ./publish/lzss-c
	$(DOTNET) publish $(LZSSE_PROJ) -c Release --self-contained true -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/ -o ./publish/lzss-e

Churn:
	$(DOTNET) publish $(CHURN_PROJ) -c Release --self-contained true -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Carman:
	$(DOTNET) build $(CARMAN_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Dct:
	$(DOTNET) build $(DCTC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(DCTE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Gs:
	$(DOTNET) build $(GS_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

# -------------------------
# Run Targets
# -------------------------
#build-debug:
#	$(DOTNET) build $(C_PROJ) -c Debug -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
#	$(DOTNET) build $(E_PROJ) -c Debug -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-compress-release:
	$(DOTNET) run --project $(C_PROJ) -c Release --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-extract-release:
	$(DOTNET) run --project $(E_PROJ) -c Release --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-compress-debug:
	$(DOTNET) run --project $(C_PROJ) -c Debug --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-extract-debug:
	$(DOTNET) run --project $(E_PROJ) -c Debug --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

# -------------------------
# Clean Target
# -------------------------
clean:
	rm -rf csfiles/$(BIN_DIR)
	rm -rf csfiles/$(OBJ_DIR)
#	$(DOTNET) clean $(C_PROJ)
#	$(DOTNET) clean $(E_PROJ)

# List available targets
list:
	@echo "Available targets:"
	@echo "  all              - Build everything"
	@echo "  Carman           - Build Carman.cs"
	@echo "  Arith            - Build Arith.cs"
	@echo "  Arith1           - Build Arith1.cs"
	@echo "  Arith1e          - Build Arith1e.cs"
	@echo "  Lzw12            - Build Lzw12.cs"
	@echo "  Lzw15v           - Build Lzw15v.cs"
	@echo "  Lzss             - Build Lzss.cs"
	@echo "  Churn            - Build Churn.cs"
	@echo "  Dct              - Build Dct.cs"
	@echo "  Gs               - Build Gs.cs"
	@echo "  Huff             - Build Huff.cs"
	@echo "  Ahuff            - Build Ahuff.cs"
	@echo "  Compand          - Build Compand.cs"
	@echo "  Silence          - Build Silence.cs"
	@echo "  run-carman       - Run Carman project"
	@echo "  run-*            - Run individual programs"
	@echo "  clean            - Clean all build artifacts"


.PHONY: all clean list run-carman run-churn run-arith-c run-arith-e
