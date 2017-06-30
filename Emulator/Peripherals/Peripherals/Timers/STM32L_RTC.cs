﻿//
// Copyright (c) Antmicro
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using Emul8.Peripherals.Bus;
using Emul8.Logging;
using Emul8.Core.Structure.Registers;
using System.Collections.Generic;
using Emul8.Core;
using Emul8.Peripherals.Timers;

namespace Emul8.Peripherals
{
    public class STM32L_RTC : LimitTimer, IDoubleWordPeripheral
    {
        public STM32L_RTC(Machine machine) : base(machine, 1000, eventEnabled: true, autoUpdate: true) // this frequency is a blind guess
        {
            LimitReached += () =>
            {
                if(wakeupTimerInterruptEnable.Value)
                {
                    wakeupTimerFlag.Value = true;
                    this.DebugLog("Setting IRQ");
                    IRQ.Set();
                }
            };
            SetupRegisters();
        }

        public override void Reset()
        {
            base.Reset();
            registers.Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public GPIO IRQ { get; private set; }

        private void SetupRegisters()
        {
            var control = new DoubleWordRegister(this)
                .WithEnumField<DoubleWordRegister, ClockMode>(0, 3, name: "WUCKSEL", writeCallback: (_, value) =>
            {
                this.DebugLog("Setting wucksel to {0}", value);
                // for now we ignore changing clock frequency
            })
                .WithFlag(10, name: "WUTE", writeCallback: (_, value) =>
            {
                this.DebugLog(value ? "Enabling timer" : "Disabling timer");
                Enabled = value;
            });
            wakeupTimerInterruptEnable = control.DefineFlagField(14, name: "WUTIE");

            var initializationStatus = new DoubleWordRegister(this, 0x7);
            wakeupTimerFlag = initializationStatus.DefineFlagField(10, FieldMode.Read | FieldMode.WriteZeroToClear, name: "WUTF", writeCallback: (_, value) =>
            {
                if(!value)
                {
                    this.DebugLog("Clearing IRQ");
                    IRQ.Unset();
                }
            });

            var wakeupTimer = new DoubleWordRegister(this).WithValueField(0, 16, name: "WUT", writeCallback: (_, value) =>
            {
                this.DebugLog("Setting limit to {0}", value);
                Limit = value;
            });

            var registerDictionary = new Dictionary<long, DoubleWordRegister> { 
                { (long)Registers.Control, control },
                { (long)Registers.InitializationStatus, initializationStatus },
                { (long)Registers.WakeupTimer, wakeupTimer }
            };
            registers = new DoubleWordRegisterCollection(this, registerDictionary);
        }

        private enum ClockMode
        {
            RTC_16,
            RTC_8,
            RTC_4,
            RTC_2,
            CK_SPRE1,
            CK_SPRE2,
            CK_SPRE3,
            CK_SPRE4
        }

        private enum Registers
        {
            Control = 0x8,
            InitializationStatus = 0xC,
            WakeupTimer = 0x14
        }

        private DoubleWordRegisterCollection registers;
        private IFlagRegisterField wakeupTimerFlag;
        private IFlagRegisterField wakeupTimerInterruptEnable;
    }
}

