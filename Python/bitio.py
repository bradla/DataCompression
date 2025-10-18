#Bradford Arrington 2025
import sys
from io import FileIO, SEEK_SET, SEEK_CUR


class CompressorBitio:
    PACIFIER_COUNT = 2047

    class BitFile:
        def __init__(self, name: str, input_mode: bool):
            self.is_input = input_mode
            mode = "rb" if input_mode else "wb"
            self.file_stream: FileIO = open(name, mode)
            self.rack: int = 0
            self.mask: int = 0x80
            self.pacifier_counter: int = 0
            self._buffer: int = 0
            self._bit_count: int = 0

        @staticmethod
        def open_output_bit_file(name: str) -> 'CompressorBitio.BitFile':
            return CompressorBitio.BitFile(name, False)

        @staticmethod
        def open_input_bit_file(name: str) -> 'CompressorBitio.BitFile':
            return CompressorBitio.BitFile(name, True)

        def close_bit_file(self):
            if not self.is_input and self.mask != 0x80:
                try:
                    self.file_stream.write(bytes([self.rack]))
                except IOError as e:
                    raise Exception(f"Fatal error in CloseBitFile! {e}")
            self.file_stream.close()

        def output_bit(self, bit: int):
            if bit != 0:
                self.rack |= self.mask
            self.mask >>= 1
            if self.mask == 0:
                try:
                    self.file_stream.write(bytes([self.rack]))
                    self.pacifier_counter += 1
                    if (self.pacifier_counter & CompressorBitio.PACIFIER_COUNT) == 0:
                        sys.stdout.write(".")
                        sys.stdout.flush()
                except IOError as e:
                    raise Exception(f"Fatal error in OutputBit! {e}")
                self.rack = 0
                self.mask = 0x80

        def output_bits(self, code: int, count: int):
            mask_code: int = 1 << (count - 1)
            while mask_code != 0:
                if (mask_code & code) != 0:
                    self.rack |= self.mask
                self.mask >>= 1
                if self.mask == 0:
                    try:
                        self.file_stream.write(bytes([self.rack]))
                        self.pacifier_counter += 1
                        if (self.pacifier_counter & CompressorBitio.PACIFIER_COUNT) == 0:
                            sys.stdout.write(".")
                            sys.stdout.flush()
                    except IOError as e:
                        raise Exception(f"Fatal error in OutputBit! {e}")
                    self.rack = 0
                    self.mask = 0x80
                mask_code >>= 1

        def input_bit(self) -> int:
            if self.mask == 0x80:
                read = self.file_stream.read(1)
                if not read:
                    raise Exception("Fatal error in InputBit! End of file reached.")
                self.rack = ord(read)
                self.pacifier_counter += 1
                if (self.pacifier_counter & CompressorBitio.PACIFIER_COUNT) == 0:
                    sys.stdout.write(".")
                    sys.stdout.flush()
            value = self.rack & self.mask
            self.mask >>= 1
            if self.mask == 0:
                self.mask = 0x80
            return 1 if value != 0 else 0

        def read_bits(self, bits: int) -> int:
            value: int = 0
            while bits > 0:
                if self._bit_count == 0:
                    next_byte = self.file_stream.read(1)
                    if not next_byte:
                        raise EOFError()
                    self._buffer = ord(next_byte)
                    self._bit_count = 8

                shift: int = min(bits, self._bit_count)
                value = (value << shift) | ((self._buffer >> (self._bit_count - shift)) & ((1 << shift) - 1))
                self._bit_count -= shift
                bits -= shift
            return value

        def input_bits(self, bit_count: int) -> int:
            mask_code: int = 1 << (bit_count - 1)
            return_value: int = 0
            while mask_code != 0:
                if self.mask == 0x80:
                    read = self.file_stream.read(1)
                    if not read:
                        raise Exception("Fatal error in InputBit! End of file reached.")
                    self.rack = ord(read)
                    self.pacifier_counter += 1
                    if (self.pacifier_counter & CompressorBitio.PACIFIER_COUNT) == 0:
                        sys.stdout.write(".")
                        sys.stdout.flush()
                if (self.rack & self.mask) != 0:
                    return_value |= mask_code
                mask_code >>= 1
                self.mask >>= 1
                if self.mask == 0:
                    self.mask = 0x80
            return return_value
