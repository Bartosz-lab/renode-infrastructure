//
// Copyright (c) 2010-2020 Antmicro
// Copyright (c) 2020 Hugh Breslin <Hugh.Breslin@microchip.com>
//
//  This file is licensed under the MIT License.
//  Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Linq;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Sensors
{
    public class PAC1934 : II2CPeripheral
    {
        public PAC1934()
        {
            channels = new Channel[ChannelCount];
            for(var i = 0; i < ChannelCount; ++i)
            {
                channels[i] = new Channel(this, i);
            }

            var registersMap = new Dictionary<long, ByteRegister>();
            registersMap.Add(
                (long)Registers.ChannelDisable,
                new ByteRegister(this)
                    .WithReservedBits(0, 1)
                    .WithFlag(1, out noSkipInactiveChannels, name: "NO_SKIP")
                    .WithTag("BYTE_COUNT", 2, 1)
                    .WithTag("TIMEOUT", 3, 1)
                    .WithFlag(4, out channels[3].IsChannelDisabled, name: "CH4")
                    .WithFlag(5, out channels[2].IsChannelDisabled, name: "CH3")
                    .WithFlag(6, out channels[1].IsChannelDisabled, name: "CH2")
                    .WithFlag(7, out channels[0].IsChannelDisabled, name: "CH1")
            );
            registersMap.Add(
                (long)Registers.ProductId,
                new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Read, valueProviderCallback: (_) => ProductId)
                );
            registersMap.Add(
                (long)Registers.ManufacturerId,
                new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Read, valueProviderCallback: (_) => ManufacturerId)
                );
            registersMap.Add(
                (long)Registers.RevisionId,
                new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Read, valueProviderCallback: (_) => RevisionId)
                );

            registers = new ByteRegisterCollection(this, registersMap);
        }

        public void FinishTransmission()
        {
        }

        public byte[] Read(int count = 1)
        {
            byte[] res = new byte[0];
            while(res.Length < count)
            {
                byte[] bytes = ReadRegister((uint)context);

                // model should send data as big endian
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);

                res = res.Concat(bytes).ToArray();
                this.Log(LogLevel.Noisy, "Read byte tab: [{0}] from {1}", 
                            BitConverter.ToString(bytes), 
                            context);
                context = NextReadRegister(context);
            }
            return res;
        }

        public void Write(byte[] data)
        {
            // First byte is always Command/Address
            context = (Registers)data[0];
            this.Log(LogLevel.Noisy, "First Write to: {0}", context);
            if(context == Registers.Refresh || context == Registers.RefreshG)
            {
                RefreshChannels(RefreshType.WithAccumulators);
            }
            else if (context == Registers.RefreshV)
            {
                RefreshChannels(RefreshType.NoAccumulators);
            } 
            else 
            {
                foreach(byte elem in data.Skip(1))
                {
                    this.Log(LogLevel.Noisy, "Write byte: 0x{0:X} to {1}", elem, context);
                    registers.Write((long)context, elem);
                    context = NextWriteRegister(context);
                    
                }
            }
        }

        public void Reset()
        {
            accumulatorCount = 0;
            context = default(Registers);
            for(var i = 0; i < ChannelCount; ++i)
            {
                channels[i].Reset();
            }
            registers.Reset();
        }

        private Registers NextWriteRegister(Registers register)
        {
            switch(register)
            {
                case Registers.Control:
                    return Registers.ChannelDisable;
                case Registers.ChannelDisable:
                    return Registers.BidirectionalCurrentMeasurement;
                case Registers.BidirectionalCurrentMeasurement:
                    return Registers.SlowMode;
                case Registers.SlowMode:
                    return Registers.Control;
                default:
                    this.Log(LogLevel.Warning, "Trying to write to read only register: {0}", register);
                    return register;
            }
        }
        private Registers NextReadRegister(Registers register)
        {
            if(register >= Registers.ProportionalPowerAccumulator1 && register <= Registers.ProportionalPower4)
            {
                uint channelNumber = ((uint)register - 3) % 4;

                // If Skip is on and channel is disabled make loop to self
                if(noSkipInactiveChannels.Value && channels[channelNumber].IsChannelDisabled.Value)
                {
                    return register;
                }
            }

            switch(register)
            {
                case Registers.Control:
                    return Registers.AccumulatorCount;
                case Registers.AccumulatorCount:
                    if(noSkipInactiveChannels.Value || !channels[0].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPowerAccumulator1;
                    }
                    goto case Registers.ProportionalPowerAccumulator1;
                case Registers.ProportionalPowerAccumulator1:
                    if(noSkipInactiveChannels.Value || !channels[1].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPowerAccumulator2;
                    }
                    goto case Registers.ProportionalPowerAccumulator2;
                case Registers.ProportionalPowerAccumulator2:
                    if(noSkipInactiveChannels.Value || !channels[2].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPowerAccumulator3;
                    }
                    goto case Registers.ProportionalPowerAccumulator3;
                case Registers.ProportionalPowerAccumulator3:
                    if(noSkipInactiveChannels.Value || !channels[3].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPowerAccumulator4;
                    }
                    goto case Registers.ProportionalPowerAccumulator4;
                case Registers.ProportionalPowerAccumulator4:
                    if(noSkipInactiveChannels.Value || !channels[0].IsChannelDisabled.Value)
                    {
                        return Registers.BusVoltage1;
                    }
                    goto case Registers.BusVoltage1;
                case Registers.BusVoltage1:
                    if(noSkipInactiveChannels.Value || !channels[1].IsChannelDisabled.Value)
                    {
                        return Registers.BusVoltage2;
                    }
                    goto case Registers.BusVoltage2;
                case Registers.BusVoltage2:
                    if(noSkipInactiveChannels.Value || !channels[2].IsChannelDisabled.Value)
                    {
                        return Registers.BusVoltage3;
                    }
                    goto case Registers.BusVoltage3;
                case Registers.BusVoltage3:
                    if(noSkipInactiveChannels.Value || !channels[3].IsChannelDisabled.Value)
                    {
                        return Registers.BusVoltage4;
                    }
                    goto case Registers.BusVoltage4;
                case Registers.BusVoltage4:
                    if(noSkipInactiveChannels.Value || !channels[0].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorVoltage1;
                    }
                    goto case Registers.SenseResistorVoltage1;
                case Registers.SenseResistorVoltage1:
                    if(noSkipInactiveChannels.Value || !channels[1].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorVoltage2;
                    }
                    goto case Registers.SenseResistorVoltage2;
                case Registers.SenseResistorVoltage2:
                    if(noSkipInactiveChannels.Value || !channels[2].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorVoltage3;
                    }
                    goto case Registers.SenseResistorVoltage3;
                case Registers.SenseResistorVoltage3:
                    if(noSkipInactiveChannels.Value || !channels[3].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorVoltage4;
                    }
                    goto case Registers.SenseResistorVoltage4;
                case Registers.SenseResistorVoltage4:
                    if(noSkipInactiveChannels.Value || !channels[0].IsChannelDisabled.Value)
                    {
                        return Registers.AverageBusVoltage1;
                    }
                    goto case Registers.ProportionalPowerAccumulator1;
                case Registers.AverageBusVoltage1:
                    if(noSkipInactiveChannels.Value || !channels[1].IsChannelDisabled.Value)
                    {
                        return Registers.AverageBusVoltage2;
                    }
                    goto case Registers.AverageBusVoltage2;
                case Registers.AverageBusVoltage2:
                    if(noSkipInactiveChannels.Value || !channels[2].IsChannelDisabled.Value)
                    {
                        return Registers.AverageBusVoltage3;
                    }
                    goto case Registers.AverageBusVoltage3;
                case Registers.AverageBusVoltage3:
                    if(noSkipInactiveChannels.Value || !channels[3].IsChannelDisabled.Value)
                    {
                        return Registers.AverageBusVoltage4;
                    }
                    goto case Registers.AverageBusVoltage4;
                case Registers.AverageBusVoltage4:
                    if(noSkipInactiveChannels.Value || !channels[0].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorAverageVoltage1;
                    }
                    goto case Registers.SenseResistorAverageVoltage1;
                case Registers.SenseResistorAverageVoltage1:
                    if(noSkipInactiveChannels.Value || !channels[1].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorAverageVoltage2;
                    }
                    goto case Registers.SenseResistorAverageVoltage2;
                case Registers.SenseResistorAverageVoltage2:
                    if(noSkipInactiveChannels.Value || !channels[2].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorAverageVoltage3;
                    }
                    goto case Registers.SenseResistorAverageVoltage3;
                case Registers.SenseResistorAverageVoltage3:
                    if(noSkipInactiveChannels.Value || !channels[3].IsChannelDisabled.Value)
                    {
                        return Registers.SenseResistorAverageVoltage4;
                    }
                    goto case Registers.SenseResistorAverageVoltage4;
                case Registers.SenseResistorAverageVoltage4:
                    if(noSkipInactiveChannels.Value || !channels[0].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPower1;
                    }
                    goto case Registers.ProportionalPower1;
                case Registers.ProportionalPower1:
                    if(noSkipInactiveChannels.Value || !channels[1].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPower2;
                    }
                    goto case Registers.ProportionalPower2;
                case Registers.ProportionalPower2:
                    if(noSkipInactiveChannels.Value || !channels[2].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPower3;
                    }
                    goto case Registers.ProportionalPower3;
                case Registers.ProportionalPower3:
                    if(noSkipInactiveChannels.Value || !channels[3].IsChannelDisabled.Value)
                    {
                        return Registers.ProportionalPower4;
                    }
                    goto case Registers.ProportionalPower4;
                case Registers.ProportionalPower4:
                    return Registers.ChannelDisable;
                case Registers.ChannelDisable:
                    return Registers.BidirectionalCurrentMeasurement;
                case Registers.BidirectionalCurrentMeasurement:
                    return Registers.SlowMode;
                case Registers.SlowMode:
                    return Registers.ControlImage;
                case Registers.ControlImage:
                    return Registers.ChannelDisableImage;
                case Registers.ChannelDisableImage:
                    return Registers.BidirectionalCurrentMeasurementImage;
                case Registers.BidirectionalCurrentMeasurementImage:
                    return Registers.ControlPreviousImage;
                case Registers.ControlPreviousImage:
                    return Registers.ChannelDisablePreviousImage;
                case Registers.ChannelDisablePreviousImage:
                    return Registers.BidirectionalCurrentMeasurementPreviousImage;
                case Registers.BidirectionalCurrentMeasurementPreviousImage:
                    return Registers.ProductId;
                case Registers.ProductId:
                    return Registers.ManufacturerId;
                case Registers.ManufacturerId:
                    return Registers.RevisionId;
                case Registers.RevisionId:
                    return Registers.Control;
                    
                default:
                    this.Log(LogLevel.Warning, "Trying to read write only register: {0}", register);
                    return register;
            }
        }

        private byte[] ReadRegister(uint offset)
        {
            if(offset >= (uint)Registers.ProportionalPowerAccumulator1 && offset <= (uint)Registers.ProportionalPower4)
            {
                var channelNumber = (offset - 3) % 4;
                return channels[channelNumber].GetBytesFromChannelOffset(offset - channelNumber);
            }
            if(offset == (uint)Registers.AccumulatorCount)
            {
                return BitConverter.GetBytes(accumulatorCount);
            }
            return new[] {(byte)registers.Read(offset)};
        }

        private void RefreshChannels(RefreshType refresh)
        {
            for(int i = 0; i < ChannelCount; ++i)
            {
                if(channels[i].IsChannelDisabled.Value && !noSkipInactiveChannels.Value)
                {
                    channels[i].RefreshInactiveChannel(refresh);
                }
                else if(!channels[i].IsChannelDisabled.Value)
                {
                    channels[i].RefreshActiveChannel(refresh);
                    accumulatorCount += (refresh == RefreshType.WithAccumulators ? 1u : 0);
                }
            }
        }

        private Registers context;
        private uint accumulatorCount;

        private readonly Channel[] channels;
        private readonly ByteRegisterCollection registers;

        private readonly IFlagRegisterField noSkipInactiveChannels;

        private const byte ProductId = 0x5B;
        private const byte ManufacturerId = 0x5D;
        private const byte RevisionId = 0x3;

        private const int ChannelCount = 4;
        private const int ShiftBetweenChannelRegisters = 4;

        private class Channel
        {
            public Channel(PAC1934 parent, int number)
            {
                this.parent = parent;
                channelNumber = number;
                vBusQueue = new Queue<ushort>();
                vSenseQueue = new Queue<ushort>();
            }

            public byte[] GetBytesFromChannelOffset(long offset)
            {
                switch(offset)
                {
                    case (long)Registers.ProportionalPowerAccumulator1:
                        // this field has 6 bytes
                        byte[] bytes = BitConverter.GetBytes(proportionalPowerAccumulator);
                        
                        if (BitConverter.IsLittleEndian)
                            bytes = bytes.Take(bytes.Length-2).ToArray();
                        else
                            bytes = bytes.Skip(2).ToArray();
                        return bytes;
                    case (long)Registers.BusVoltage1:
                        return BitConverter.GetBytes(busVoltage);
                    case (long)Registers.SenseResistorVoltage1:
                        return BitConverter.GetBytes(senseResistorVoltage);
                    case (long)Registers.AverageBusVoltage1:
                        return BitConverter.GetBytes(averageBusVoltage);
                    case (long)Registers.SenseResistorAverageVoltage1:
                        return BitConverter.GetBytes(senseResistorAverageVoltage);
                    case (long)Registers.ProportionalPower1:
                        return BitConverter.GetBytes(proportionalPower);
                    default:
                        parent.Log(LogLevel.Warning, "Trying to read bytes from unhandled channel {0} at offset 0x{1:X}", channelNumber, offset);
                        return new byte[] { 0 };
                }
            }

            public void Reset()
            {
                busVoltage = 0;
                proportionalPower = 0;
                averageBusVoltage = 0;
                senseResistorVoltage = 0;
                senseResistorAverageVoltage = 0;
                proportionalPowerAccumulator = 0;
            }

            public void RefreshActiveChannel(RefreshType refresh)
            {
                // populate the registers with dummy data
                var randomizer = EmulationManager.Instance.CurrentEmulation.RandomGenerator;

                busVoltage = (ushort)(SampleBusVoltage + randomizer.Next(-20, 20));
                senseResistorVoltage = (ushort)(SampleSenseResistorVoltage + randomizer.Next(-20, 20));
                averageBusVoltage = GetAverage(vBusQueue, busVoltage);
                senseResistorAverageVoltage = GetAverage(vSenseQueue, senseResistorVoltage);

                proportionalPower = (uint)busVoltage * (uint)senseResistorVoltage;
                if(refresh == RefreshType.WithAccumulators)
                {
                    proportionalPowerAccumulator += proportionalPower;
                }
            }

            public void RefreshInactiveChannel(RefreshType refresh)
            {
                if(refresh == RefreshType.WithAccumulators)
                {
                    proportionalPowerAccumulator = 0xFFFFFFFFFFFF;
                }
                busVoltage = 0xFFFF;
                senseResistorVoltage = 0xFFFF;
                averageBusVoltage = 0xFFFF;
                senseResistorAverageVoltage = 0xFFFF;
                proportionalPower = 0xFFFFFFF;
            }

            public IFlagRegisterField IsChannelDisabled;

            private ushort GetAverage(Queue<ushort> queue, ushort value)
            {
                var result = 0u;
                if(queue.Count == 8)
                {
                    queue.Dequeue();
                }
                queue.Enqueue(value);
                foreach(var val in queue)
                {
                    result += val;
                }
                return (ushort)(result / queue.Count);
            }

            private readonly Queue<ushort> vSenseQueue;
            private readonly Queue<ushort> vBusQueue;
            private readonly int channelNumber;
            private readonly PAC1934 parent;
            private ulong proportionalPowerAccumulator;
            private ushort busVoltage;
            private ushort senseResistorVoltage;
            private ushort averageBusVoltage;
            private ushort senseResistorAverageVoltage;
            private uint proportionalPower;

            private const ushort SampleBusVoltage = 3500;
            private const ushort SampleSenseResistorVoltage = 3500;
        }

        private enum Registers : byte
        {
            // General Registers
            Refresh = 0x00,
            Control = 0x1,
            AccumulatorCount = 0x2,

            // Channel Registers
            ProportionalPowerAccumulator1 = 0x3,
            ProportionalPowerAccumulator2 = 0x4,
            ProportionalPowerAccumulator3 = 0x5,
            ProportionalPowerAccumulator4 = 0x6,
            BusVoltage1 = 0x7,
            BusVoltage2 = 0x8,
            BusVoltage3 = 0x9,
            BusVoltage4 = 0xA,
            SenseResistorVoltage1 = 0xB,
            SenseResistorVoltage2 = 0xC,
            SenseResistorVoltage3 = 0xD,
            SenseResistorVoltage4 = 0xE,
            AverageBusVoltage1 = 0xF,
            AverageBusVoltage2 = 0x10,
            AverageBusVoltage3 = 0x11,
            AverageBusVoltage4 = 0x12,
            SenseResistorAverageVoltage1 = 0x13,
            SenseResistorAverageVoltage2 = 0x14,
            SenseResistorAverageVoltage3 = 0x15,
            SenseResistorAverageVoltage4 = 0x16,
            ProportionalPower1 = 0x17,
            ProportionalPower2 = 0x18,
            ProportionalPower3 = 0x19,
            ProportionalPower4 = 0x1A,

            // General Registers
            ChannelDisable = 0x1C,
            BidirectionalCurrentMeasurement = 0x1D,
            RefreshG = 0x1E,
            RefreshV = 0x1F,
            SlowMode = 0x20,
            ControlImage = 0x21,
            ChannelDisableImage = 0x22,
            BidirectionalCurrentMeasurementImage = 0x23,
            ControlPreviousImage = 0x24,
            ChannelDisablePreviousImage = 0x25,
            BidirectionalCurrentMeasurementPreviousImage = 0x26,
            ProductId = 0xFD,
            ManufacturerId = 0xFE,
            RevisionId = 0xFF
        }

        private enum RefreshType : byte
        {
            NoAccumulators = 0,
            WithAccumulators = 1
        }
    }
}
