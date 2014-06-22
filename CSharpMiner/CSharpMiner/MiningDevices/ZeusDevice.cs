﻿using CSharpMiner.Helpers;
using CSharpMiner.Stratum;
using System;
using System.IO.Ports;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace MiningDevice
{
    [DataContract]
    class ZeusDevice : UsbMinerBase
    {
        private int _clk;
        [DataMember(Name = "clock")]
        public int LtcClk 
        { 
            get
            {
                return _clk;
            }

            set
            {
                if (value > 382)
                    _clk = 382;
                else if (value < 2)
                    _clk = 2;
                else
                    _clk = value;

                byte _freqCode = (byte)(_clk * 2 / 3);

                byte[] cmd = CommandPacket;
                cmd[0] = _freqCode;
                cmd[1] = (byte)(0xFF - _freqCode);
            }
        }

        private byte[] _eventPacket = null;
        [IgnoreDataMember]
        private byte[] EventPacket
        {
            get
            {
                if (_eventPacket == null)
                    _eventPacket = new byte[4];

                return _eventPacket;
            }
        }

        private byte[] _commandPacket = null;
        [IgnoreDataMember]
        private byte[] CommandPacket
        {
            get
            {
                if (_commandPacket == null)
                    _commandPacket = new byte[84];

                return _commandPacket;
            }
        }

        private PoolWork currentWork = null;

        public ZeusDevice(string port, int clk, int cores)
        {
            UARTPort = port;
            LtcClk = clk;
            Cores = cores;
        }

        public override void StartWork(PoolWork work)
        {
            this.RestartWatchdogTimer();

            if (this.usbPort != null && this.usbPort.IsOpen)
            {
                LogHelper.ConsoleLogAsync(string.Format("Device {0} starting work {1}.", this.UARTPort, work.JobId), LogVerbosity.Verbose);

                int diffCode = 0xFFFF / work.Diff;
                byte[] cmd = CommandPacket;

                cmd[3] = (byte)diffCode;
                cmd[2] = (byte)(diffCode >> 8);

                int offset = 4;

                // Starting nonce
                byte[] nonceBytes = HexConversionHelper.ConvertFromHexString(HexConversionHelper.Swap(string.Format("{0,8:X8}", work.StartingNonce)));
                CopyToByteArray(nonceBytes, offset, cmd);
                offset += nonceBytes.Length;

                byte[] headerBytes = HexConversionHelper.ConvertFromHexString(HexConversionHelper.Reverse(work.Header));
                CopyToByteArray(headerBytes, offset, cmd);

                LogHelper.DebugConsoleLogAsync(string.Format("{0} getting: {1}", UARTPort, HexConversionHelper.ConvertToHexString(cmd)));

                // Send work to the miner
                this.currentWork = work;
                this.usbPort.DiscardInBuffer();
                this.usbPort.Write(cmd, 0, cmd.Length);
            }
            else
            {
                LogHelper.DebugConsoleLogAsync(string.Format("Device {0} pending work {1}.", this.UARTPort, work.JobId), LogVerbosity.Verbose);

                this.pendingWork = work;
            }
        }

        private void CopyToByteArray(byte[] src, int offset, byte[] dest)
        {
            for (int i = 0; i < src.Length; i++)
            {
                dest[i + offset] = src[i];
            }
        }

        public override int GetBaud()
        {
            return 115200;
        }

        protected override void DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort sp = sender as SerialPort;

            if(sp != null)
            {
                while (sp.BytesToRead >= 4)
                {
                    this.RestartWatchdogTimer();
                    sp.Read(EventPacket, 0, 4);
                    ProcessEventPacket(EventPacket);
                }
            } 
        }

        private void ProcessEventPacket(byte[] packet)
        {
            if(currentWork != null)
            {
                Task.Factory.StartNew(() =>
                    {
                        LogHelper.ConsoleLogAsync(string.Format("Device {0} submitting {1} for job {2}.", this.UARTPort, HexConversionHelper.ConvertToHexString(packet), (this.currentWork != null ? this.currentWork.JobId : "null")), ConsoleColor.DarkCyan);
                        this.SubmitWork(currentWork, HexConversionHelper.Swap(HexConversionHelper.ConvertToHexString(packet)));
                    });
            }
        }
    }
}
