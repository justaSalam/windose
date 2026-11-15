using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Cosmos.Kernel.HAL.Devices.Audio;
using Cosmos.Kernel.HAL.DriverKit;
using Cosmos.Kernel.HAL.DriverKit.Buses.Pci;
using Cosmos.Kernel.HAL.DriverKit.Interrupts;
using Cosmos.Kernel.HAL.DriverKit.Resources;
using Cosmos.Kernel.HAL.DriverKit.Threading;

namespace Windose.System.Drivers.Audio
{
    /// <summary>
    /// Driver for the Ensoniq AudioPCI / Creative Sound Blaster PCI 128 (ES1371 / ES1373 / CT5880).
    /// This is the primary virtual audio card emulated by VMware Workstation, Player, and Fusion.
    /// Provides 16-bit stereo PCM audio playback integrated into the Cosmos Audio subsystem.
    /// </summary>
    [Experimental("COSMOS0003")]
    [Driver(Feature = DriverFeature.Audio)]
    public sealed class Es1371AudioDriver : Driver
    {
        // PCI IDs for Creative / Ensoniq Sound Blaster PCI
        private const ushort VendorIdEnsoniq = 0x1274;
        private const ushort DeviceIdEs1371 = 0x1371; // Ensoniq AudioPCI 97 / Sound Blaster PCI 128 (VMware default)
        private const ushort DeviceIdCt5880 = 0x5880; // Creative CT5880 / ES1373

        private readonly DeviceMatch[] _matches = new DeviceMatch[]
        {
            new PciMatch(VendorIdEnsoniq, DeviceIdEs1371, null, null, null, null, null, null),
            new PciMatch(VendorIdEnsoniq, DeviceIdCt5880, null, null, null, null, null, null)
        };

        public override string Name => "Es1371AudioDriver";
        public override ReadOnlySpan<DeviceMatch> Matches => _matches;

        public override ProbeResult Probe(DeviceBinding binding)
        {
            PciAccess pciAccess = binding.Node.Access<PciAccess>();
            if (pciAccess.Bars.Length == 0)
            {
                return ProbeResult.Declined("PCI device has no BARs");
            }

            PciBar bar0 = pciAccess.Bars[0];
            if (!bar0.IsAssigned || bar0.Length < 64)
            {
                return ProbeResult.Declined("BAR0 is not assigned or too small for ES1371 registers");
            }

            RegisterWindow registers = binding.MapRegisters(0);
            pciAccess.EnableIoSpace(true);
            pciAccess.EnableBusMastering(true);

            // Allocate cyclic DMA ring buffer (16 KB, 64-byte aligned) in 32-bit address space
            DmaBuffer ring = binding.AllocateDma(16384, 64);
            if (ring.PhysicalAddress >= 0x100000000UL)
            {
                return ProbeResult.Failed("DMA buffer was allocated above 4 GiB boundary");
            }

            // Create device state
            Es1371AudioState state = new Es1371AudioState(binding, registers, ring);
            if (!state.Initialize())
            {
                return ProbeResult.Failed("Failed to initialize ES1371 hardware and AC'97 codec");
            }

            // Hook legacy line interrupt if available
            if (binding.Node.Interrupts.Count > 0)
            {
                binding.TryRequestInterrupt(binding.Node.Interrupts[0], state.OnInterrupt, out _);
            }

            AudioSink sink = binding.PublishAudio(state);
            state.AttachSink(sink);
            binding.DriverState = state;

            return ProbeResult.Bound;
        }

        public override void OnDetach(DeviceBinding binding, DetachReason reason)
        {
            if (binding.DriverState is Es1371AudioState state)
            {
                state.Quiesce();
            }
        }
    }

    /// <summary>
    /// Hardware controller state and IAudioOutput implementation for ES1371 DAC2.
    /// </summary>
    [Experimental("COSMOS0003")]
    internal sealed class Es1371AudioState : IAudioOutput
    {
        // ES1371 Direct Register Offsets (BAR0)
        private const ulong RegControl = 0x00;        // R/W: Interrupt / Chip select control
        private const ulong RegStatus = 0x04;         // R/O: Interrupt status
        private const ulong RegMemPage = 0x0C;        // R/W: Memory page register
        private const ulong RegSrc = 0x10;            // R/W: Sample Rate Converter (SRC) interface
        private const ulong RegCodec = 0x14;          // R/W: AC'97 Codec interface
        private const ulong RegSerialControl = 0x20;  // R/W: Serial interface control
        private const ulong RegDac2Count = 0x28;      // R/W: DAC2 sample/period count reload register

        // Memory Page 0x0C registers (DAC channels)
        private const ulong RegDac2Addr = 0x38;       // DAC2 frame address
        private const ulong RegDac2Size = 0x3C;       // DAC2 frame size / sample count

        // Control Register (0x00) Bits
        private const uint CtrlBreq = 0x00000080;     // Bit 7: Memory bus request enable (Required for DMA)
        private const uint CtrlDac1En = 0x00000040;   // Bit 6: DAC1 playback channel enable
        private const uint CtrlDac2En = 0x00000020;   // Bit 5: DAC2 playback channel enable
        private const uint CtrlAdcEn = 0x00000010;    // Bit 4: ADC capture channel enable

        // Serial Interface Control Register (0x20) Bits
        private const uint SctrlP2FmtStereo16 = 0x0000000C; // Bits 2..3: DAC2 16-bit Stereo (3 << 2)
        private const uint SctrlP2IntEn = 0x00000200;       // Bit 9: DAC2 Interrupt Enable
        private const uint SctrlP2Pause = 0x00001000;       // Bit 12: DAC2 Pause (1 = pause, 0 = play)
        private const uint SctrlP2LoopSel = 0x00004000;     // Bit 14: DAC2 Loop (1 = stop, 0 = loop)
        private const uint SctrlP2EndIncStereo16 = 0x00100000; // Bits 19..21: End increment (2 << 19 for 16-bit)

        // Status Register (0x04) Bits
        private const uint StatDac2Int = 0x00000002;        // Bit 1: DAC2 interrupt pending (ES_DAC2 in Linux)
        private const uint StatIntrPending = 0x80000000;    // Bit 31: Global interrupt pending

        // Codec (0x14) & SRC (0x10) Register Bits
        private const uint CodecWip = 0x40000000;     // Bit 30: Codec Write In Progress
        private const uint SrcRamBusy = 0x00800000;   // Bit 23: SRC RAM Busy
        private const uint SrcRamWe = 0x01000000;     // Bit 24: SRC RAM Write Enable
        private const uint SrcDisable = 0x00400000;   // Bit 22: Disable SRC
        private const uint SrcDisP2 = 0x00100000;     // Bit 20: Disable DAC2 SRC
        private const uint SrcDisP1 = 0x00080000;     // Bit 19: Disable DAC1 SRC
        private const uint SrcDisR1 = 0x00040000;     // Bit 18: Disable ADC SRC

        // Sample Rate Converter (SRC) Internal RAM Addresses
        private const ushort SrcVolDac1 = 0x7C;       // DAC1 Left volume
        private const ushort SrcVolDac2 = 0x7E;       // DAC2 Left volume (0x7F is Right)
        private const ushort SrcVolAdc = 0x6C;        // ADC Left volume
        private const ushort SrcDac2 = 0x74;          // DAC2 base address in SRC RAM
        private const ushort SrcTruncN = 0x00;        // Truncation / loop parameters
        private const ushort SrcIntRegs = 0x01;       // Integer frequency registers
        private const ushort SrcVfreqFrac = 0x03;     // Fractional frequency registers

        // AC'97 Mixer Register Addresses
        private const ushort Ac97MasterVolume = 0x02;
        private const ushort Ac97HeadphoneVolume = 0x04;
        private const ushort Ac97MasterMonoVolume = 0x06;
        private const ushort Ac97PcmVolume = 0x18;

        // Buffer Constants
        private const int RingBytes = 16384;
        private const int PeriodBytes = 4096;
        private const int GuardBytes = 2048;

        private readonly DeviceBinding _binding;
        private readonly RegisterWindow _registers;
        private readonly DmaBuffer _ring;
        private readonly DeviceLock _lock;
        private AudioSink? _sink;

        private int _writePosition;
        private bool _running;
        private uint _sampleRate = 48000;
        private AudioFormat _format = AudioFormat.Stereo16;

        private static readonly AudioFormat[] SupportedFormatList = new AudioFormat[]
        {
            AudioFormat.Stereo16
        };

        public Es1371AudioState(DeviceBinding binding, RegisterWindow registers, DmaBuffer ring)
        {
            _binding = binding;
            _registers = registers;
            _ring = ring;
            _lock = binding.CreateLock();
        }

        public string Name => "Sound Blaster PCI 128 (ES1371)";
        public AudioFormat Format => _format;
        public uint SampleRate => _sampleRate;
        public ReadOnlySpan<AudioFormat> SupportedFormats => SupportedFormatList;
        public bool IsRunning => _running;

        public int WritableBytes
        {
            get
            {
                using (_lock.Acquire())
                {
                    return FreeBytesLocked();
                }
            }
        }

        public bool Initialize()
        {
            // Clear ring buffer memory
            _ring.Span.Clear();
            DmaBuffer.WriteBarrier();

            // Set Bus Master Request enable in Control register (Bit 7: ES_BREQ)
            _registers.Write32(RegControl, CtrlBreq);

            // Configure Serial Interface Control: DAC2 16-bit Stereo, unpaused, looping mode, end increment
            uint sctrl = _registers.Read32(RegSerialControl);
            sctrl &= ~(SctrlP2Pause | SctrlP2LoopSel | 0x0000000Cu | (0x07u << 19) | (0x07u << 16));
            sctrl |= SctrlP2FmtStereo16 | SctrlP2IntEn | SctrlP2EndIncStereo16;
            _registers.Write32(RegSerialControl, sctrl);

            // Initialize Sample Rate Converter (SRC) RAM & volume multipliers
            InitSrcRam();

            // Set initial playback sample rate to 48kHz
            SetSampleRate(48000);

            // Configure AC'97 Codec mixer volumes: unmuted, 0dB gain
            CodecWrite(Ac97MasterVolume, 0x0000);     // 0dB master volume
            CodecWrite(Ac97HeadphoneVolume, 0x0000);  // 0dB headphone volume
            CodecWrite(Ac97MasterMonoVolume, 0x0000); // 0dB mono volume
            CodecWrite(Ac97PcmVolume, 0x0808);        // 0dB PCM out volume (standard AC'97 unity gain)

            // Setup DAC2 DMA buffer address and frame count under Page 0x0C
            _registers.Write32(RegMemPage, 0x0C);
            _registers.Write32(RegDac2Addr, (uint)_ring.PhysicalAddress);
            uint dwordCount = (RingBytes / 4) - 1;
            _registers.Write32(RegDac2Size, dwordCount);

            // Setup period sample count reload (4096 bytes / 4 = 1024 samples minus 1 = 1023)
            uint periodSampleCount = (PeriodBytes / 4) - 1;
            _registers.Write32(RegDac2Count, periodSampleCount);

            return true;
        }

        public void AttachSink(AudioSink sink)
        {
            _sink = sink;
        }

        public void OnInterrupt(InterruptContext context)
        {
            uint status = _registers.Read32(RegStatus);
            if ((status & StatDac2Int) != 0)
            {
                // Acknowledge DAC2 interrupt by toggling interrupt enable in SerialControl
                uint sctrl = _registers.Read32(RegSerialControl);
                _registers.Write32(RegSerialControl, sctrl & ~SctrlP2IntEn);
                _registers.Write32(RegSerialControl, sctrl | SctrlP2IntEn);
                _sink?.BufferCompleted();
            }
        }

        public bool TrySetFormat(AudioFormat format, uint sampleRate)
        {
            if (format != AudioFormat.Stereo16) return false;
            if (sampleRate < 4000 || sampleRate > 48000) return false;

            _format = format;
            _sampleRate = sampleRate;
            SetSampleRate(sampleRate);
            _sink?.FormatChanged();
            return true;
        }

        public void Start()
        {
            using (_lock.Acquire())
            {
                if (_running) return;

                // Reset write position and clear ring
                _writePosition = 0;
                _ring.Span.Clear();
                DmaBuffer.WriteBarrier();

                // Ensure DAC2 frame address, size and period count are programmed
                _registers.Write32(RegMemPage, 0x0C);
                _registers.Write32(RegDac2Addr, (uint)_ring.PhysicalAddress);
                _registers.Write32(RegDac2Size, (uint)((RingBytes / 4) - 1));
                _registers.Write32(RegDac2Count, (uint)((PeriodBytes / 4) - 1));

                // Configure Serial Control: Stereo16, IntEn, EndInc=2, unpaused, looping
                uint sctrl = _registers.Read32(RegSerialControl);
                sctrl &= ~(SctrlP2Pause | SctrlP2LoopSel | 0x0000000Cu | (0x07u << 19) | (0x07u << 16));
                sctrl |= SctrlP2FmtStereo16 | SctrlP2IntEn | SctrlP2EndIncStereo16;
                _registers.Write32(RegSerialControl, sctrl);

                // Enable DAC2 and BREQ in Control register
                uint ctrl = _registers.Read32(RegControl);
                ctrl |= CtrlBreq | CtrlDac2En;
                _registers.Write32(RegControl, ctrl);

                _running = true;
            }
        }

        public void Stop()
        {
            using (_lock.Acquire())
            {
                if (!_running) return;

                // Pause DAC2 in Serial Control register
                uint sctrl = _registers.Read32(RegSerialControl);
                sctrl |= SctrlP2Pause;
                _registers.Write32(RegSerialControl, sctrl);

                // Disable DAC2 in Control register (keep BREQ enabled)
                uint ctrl = _registers.Read32(RegControl);
                ctrl &= ~CtrlDac2En;
                ctrl |= CtrlBreq;
                _registers.Write32(RegControl, ctrl);

                _running = false;
                _writePosition = 0;
                _ring.Span.Clear();
                DmaBuffer.WriteBarrier();
            }
        }

        public int Write(ReadOnlySpan<byte> frames)
        {
            if (frames.IsEmpty) return 0;

            int frameSize = Format.FrameSize;
            using (_lock.Acquire())
            {
                int free = FreeBytesLocked();
                int count = Math.Min(frames.Length, free);
                count -= count % frameSize;
                if (count <= 0) return 0;

                Span<byte> ringSpan = _ring.Span;
                int spaceToEnd = RingBytes - _writePosition;

                if (count <= spaceToEnd)
                {
                    frames.Slice(0, count).CopyTo(ringSpan.Slice(_writePosition, count));
                }
                else
                {
                    frames.Slice(0, spaceToEnd).CopyTo(ringSpan.Slice(_writePosition, spaceToEnd));
                    int remainder = count - spaceToEnd;
                    frames.Slice(spaceToEnd, remainder).CopyTo(ringSpan.Slice(0, remainder));
                }

                DmaBuffer.WriteBarrier();
                _writePosition = (_writePosition + count) % RingBytes;
                ClearGuardAheadLocked();

                return count;
            }
        }

        public void Quiesce()
        {
            Stop();
        }

        private int FreeBytesLocked()
        {
            if (!_running) return RingBytes - GuardBytes;
            int currentPos = GetEnginePosition();
            int unread = _writePosition - currentPos;
            if (unread < 0) unread += RingBytes;
            int free = RingBytes - unread - GuardBytes;
            return free > 0 ? free : 0;
        }

        private int GetEnginePosition()
        {
            if (!_running) return 0;
            _registers.Write32(RegMemPage, 0x0C);
            uint val = _registers.Read32(RegDac2Size);
            // In ES1371, current sample count / byte position is in bits 16..31 (word count shifted)
            int currentByte = (int)(((val >> 14) & 0x3FFFCu) % RingBytes);
            return currentByte;
        }

        private void ClearGuardAheadLocked()
        {
            Span<byte> ringSpan = _ring.Span;
            int guardEnd = _writePosition + GuardBytes;
            if (guardEnd <= RingBytes)
            {
                ringSpan.Slice(_writePosition, GuardBytes).Clear();
            }
            else
            {
                int firstChunk = RingBytes - _writePosition;
                ringSpan.Slice(_writePosition, firstChunk).Clear();
                int secondChunk = GuardBytes - firstChunk;
                ringSpan.Slice(0, secondChunk).Clear();
            }
            DmaBuffer.WriteBarrier();
        }

        private void InitSrcRam()
        {
            WaitSrcReady();
            _registers.Write32(RegSrc, SrcDisable);

            // Initialize all 128 SRC RAM cells to 0
            for (ushort i = 0; i < 0x80; i++)
            {
                SrcWrite(i, 0);
            }

            // Set unity volume multiplier (1 << 12 = 0x1000 = 0dB) for all audio channels
            SrcWrite(SrcVolDac1, 0x1000);
            SrcWrite((ushort)(SrcVolDac1 + 1), 0x1000);
            SrcWrite(SrcVolDac2, 0x1000);
            SrcWrite((ushort)(SrcVolDac2 + 1), 0x1000);
            SrcWrite(SrcVolAdc, 0x1000);
            SrcWrite((ushort)(SrcVolAdc + 1), 0x1000);

            // Configure standard truncation & interpolation parameters for DAC2
            SrcWrite((ushort)(SrcDac2 + SrcTruncN), 16 << 4);
            SrcWrite((ushort)(SrcDac2 + SrcIntRegs), 16 << 10);

            // Enable SRC
            WaitSrcReady();
            _registers.Write32(RegSrc, 0);
        }

        private void SetSampleRate(uint rate)
        {
            // freq = ((rate << 15) + 1500) / 3000
            uint freq = ((rate << 15) + 1500) / 3000;

            // Temporarily disable DAC2 in SRC during rate reprogramming
            uint r = (WaitSrcReady() & (SrcDisable | SrcDisP1 | SrcDisR1)) | SrcDisP2;
            _registers.Write32(RegSrc, r);

            ushort intRegs = SrcRead((ushort)(SrcDac2 + SrcIntRegs));
            intRegs = (ushort)((intRegs & 0x00FF) | (ushort)((freq >> 5) & 0xFC00));
            SrcWrite((ushort)(SrcDac2 + SrcIntRegs), intRegs);
            SrcWrite((ushort)(SrcDac2 + SrcVfreqFrac), (ushort)(freq & 0x7FFF));

            // Re-enable DAC2 in SRC
            r = WaitSrcReady() & (SrcDisable | SrcDisP1 | SrcDisR1);
            _registers.Write32(RegSrc, r);
        }

        private void CodecWrite(ushort reg, ushort val)
        {
            // Wait for Codec Write In Progress (WIP) bit to clear
            for (int i = 0; i < 2000; i++)
            {
                if ((_registers.Read32(RegCodec) & CodecWip) == 0)
                {
                    uint srcState = WaitSrcReady();
                    _registers.Write32(RegSrc, srcState & ~SrcRamWe);

                    uint cmd = ((uint)(reg & 0x7F) << 16) | (uint)(val & 0xFFFF);
                    _registers.Write32(RegCodec, cmd);

                    WaitSrcReady();
                    _registers.Write32(RegSrc, srcState);
                    return;
                }
                _binding.Delay(5);
            }
        }

        private uint WaitSrcReady()
        {
            uint r = 0;
            for (int i = 0; i < 2000; i++)
            {
                r = _registers.Read32(RegSrc);
                if ((r & SrcRamBusy) == 0) return r;
                _binding.Delay(5);
            }
            return r;
        }

        private ushort SrcRead(ushort reg)
        {
            uint r = WaitSrcReady() & (SrcDisable | SrcDisP1 | SrcDisP2 | SrcDisR1);
            r |= ((uint)(reg & 0x7F) << 25) | 0x00010000u; // Read flag
            _registers.Write32(RegSrc, r);
            uint res = WaitSrcReady();
            return (ushort)(res & 0xFFFF);
        }

        private void SrcWrite(ushort reg, ushort val)
        {
            uint r = WaitSrcReady() & (SrcDisable | SrcDisP1 | SrcDisP2 | SrcDisR1);
            uint cmd = r | ((uint)(reg & 0x7F) << 25) | SrcRamWe | (uint)(val & 0xFFFF);
            _registers.Write32(RegSrc, cmd);
        }
    }
}
