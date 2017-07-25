﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using TL866.Properties;

namespace TL866
{
    public partial class MainForm : Form
    {
        private static readonly Color DarkGreen = Color.FromArgb(0, 64, 0);
        private static readonly Color LightGreen = Color.FromArgb(0, 255, 0);
        private static readonly Color DarkRed = Color.FromArgb(64, 0, 0);
        private static readonly Color LightRed = Color.FromArgb(255, 0, 0);
        private static readonly Color DarkYellow = Color.FromArgb(64, 64, 0);
        private static readonly Color LightYellow = Color.FromArgb(255, 255, 0);
        private AdvancedDialog AdvancedWindow;


        private string devcode;
        private int devtype;
        public Firmware firmware;
        private bool reset_flag;
        private string serial;
        public UsbDevice usb;

        private BackgroundWorker worker;

        public MainForm()
        {
            InitializeComponent();

            reset_flag = false;
            firmware = new Firmware();
            usb = new UsbDevice();
            devcode = "";
            serial = "";
            usb.UsbDeviceChanged += UsbDeviceChanged;
            usb.RegisterForDeviceChange(true, this);
            worker = new BackgroundWorker();
            worker.DoWork += Worker_DoWork;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            Leds_Off();
            UsbDeviceChanged();
        }


        //Events/////////////////////////////////////////////
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            worker.CancelAsync();
            usb.CloseDevice();
            usb.RegisterForDeviceChange(false, this);
        }


        private void BtnInput_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Update.dat";
            dlg.Filter = "dat files (*.dat)|*.dat|All files (*.*)|*.*";
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                TxtInput.Text = "";
                try
                {
                    firmware.Open(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "TL866", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    lblVersion.Text = "";
                    return;
                }
                lblVersion.Text = string.Format("[V:{0}]", firmware.Version);
            }
            TxtInput.Text = dlg.FileName;
        }

        private void BtnDefault_Click(object sender, EventArgs e)
        {
            TxtDevcode.Text = "00000000";
            TxtSerial.Text = "000000000000000000000000";
        }


        private void BtnEdit_Click(object sender, EventArgs e)
        {
            EditDialog dlg = new EditDialog();
            dlg.TxtDevcode.Text = TxtDevcode.Text;
            dlg.TxtSerial.Text = TxtSerial.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                TxtDevcode.Text = dlg.TxtDevcode.Text;
                TxtSerial.Text = dlg.TxtSerial.Text;
            }
        }


        private void BtnClone_Click(object sender, EventArgs e)
        {
            UsbDeviceChanged();
            if (!string.IsNullOrEmpty(devcode) || !string.IsNullOrEmpty(serial))
            {
                TxtDevcode.Text = devcode;
                TxtSerial.Text = serial;
            }
        }


        private void BtnDump_Click(object sender, EventArgs e)
        {
            if (!CheckDevices(this)) return;
            if (!firmware.IsValid)
            {
                MessageBox.Show(Utils.NO_FIRMWARE, "TL866", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            SaveFileDialog sdialog = new SaveFileDialog();
            sdialog.Title = "Firmware dump output file";
            sdialog.Filter = "Hex files (*.hex)|*.hex|All files (*.*)|*.*";
            sdialog.CheckPathExists = true;
            sdialog.OverwritePrompt = true;
            if (sdialog.ShowDialog() == DialogResult.OK)
            {
                UsbDeviceChanged();
                ProgressBar1.Maximum = Firmware.FLASH_SIZE; //128Kbytes
                worker.RunWorkerAsync(new object[] {device_action.dump_device, sdialog.FileName});
            }
        }


        private void BtnAdvanced_Click(object sender, EventArgs e)
        {
            AdvancedWindow = new AdvancedDialog();
            AdvancedWindow.Show(this);
        }

        private void BtnReflash_Click(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;
            if (!CheckDevices(this)) return;
            if (!firmware.IsValid)
            {
                MessageBox.Show(Utils.NO_FIRMWARE, "TL866", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }


            if (MessageBox.Show(Utils.WARNING_REFLASH, "TL866", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) ==
                DialogResult.Yes)
            {
                int ftype = (int) Firmware.FIRMWARE_TYPE.FIRMWARE_A;
                if (RadioA.Checked)
                    ftype = (int) Firmware.FIRMWARE_TYPE.FIRMWARE_A;
                if (RadioCS.Checked)
                    ftype = (int) Firmware.FIRMWARE_TYPE.FIRMWARE_CS;
                if (RadioDump.Checked)
                    ftype = (int) Firmware.FIRMWARE_TYPE.FIRMWARE_CUSTOM;
                ProgressBar1.Maximum = Firmware.ENCRYPTED_FIRMWARE_SIZE;
                worker.RunWorkerAsync(new object[] {device_action.reflash_device, ftype});
            }
        }


        private void BtnSave_Click(object sender, EventArgs e)
        {
            SaveHex(RadiofA.Checked ? Resources.firmwareA : Resources.firmwareCS);
        }


        private void BtnReset_Click(object sender, EventArgs e)
        {
            if (worker.IsBusy)
                return;
            if (!CheckDevices(this))
                return;
            worker.RunWorkerAsync(new object[] {device_action.reset_device});
        }


        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            switch ((led_action) e.ProgressPercentage)
            {
                case led_action.led_erase_on:
                    LedErase.BackColor = LightYellow;
                    break;
                case led_action.led_erase_off:
                    LedErase.BackColor = DarkYellow;
                    break;
                case led_action.led_write_on:
                    LedWrite.BackColor = LightRed;
                    break;
                case led_action.led_write_off:
                    LedWrite.BackColor = DarkRed;
                    break;
            }
        }


        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            object[] parameters = e.Argument as object[];
            switch ((device_action) parameters[0])
            {
                case device_action.reset_device:
                    Reset_Device();
                    break;
                case device_action.reflash_device:
                    Reflash((int) parameters[1]);
                    break;
                case device_action.dump_device:
                    Dump(parameters[1].ToString());
                    break;
            }
        }


        public void UsbDeviceChanged()
        {
            if (usb.DevicesCount == 0 && reset_flag)
                return;
            reset_flag = false;
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(assembly.Location);
            Text = string.Format("TL866 firmware updater  V{0}   ({1} {2} connected)", version.FileVersion,
                usb.DevicesCount,
                usb.DevicesCount == 1 ? "device" : "devices");
            if (usb.DevicesCount > 0 && usb.OpenDevice(usb.Get_Devices()[0]))
            {
                TL866_Report tl866_report = new TL866_Report();
                Get_Report(tl866_report);
                TxtInfo.Clear();
                switch (tl866_report.Device_Version)
                {
                    case 1:
                        TxtInfo.AppendText("Device version: TL866A\n");
                        devtype = (int) Firmware.PROGRAMMER_TYPE.TL866A;
                        break;
                    case 2:
                        TxtInfo.AppendText("Device version: TL866CS\n");
                        devtype = (int) Firmware.PROGRAMMER_TYPE.TL866CS;
                        break;
                    default:
                        TxtInfo.AppendText("Device version: Unknown\n");
                        break;
                }
                switch ((Firmware.DEVICE_STATUS) tl866_report.Device_Status)
                {
                    case Firmware.DEVICE_STATUS.NORMAL_MODE:
                        TxtInfo.AppendText("Device status: Normal working mode.\n");
                        LedNorm.BackColor = LightGreen;
                        LedBoot.BackColor = DarkGreen;
                        break;
                    case Firmware.DEVICE_STATUS.BOOTLOADER_MODE:
                        TxtInfo.AppendText("Device status: Bootloader mode <waiting for update.>\n");
                        LedNorm.BackColor = DarkGreen;
                        LedBoot.BackColor = LightGreen;
                        break;
                    default:
                        TxtInfo.AppendText("Device status: Unknown\n");
                        LedNorm.BackColor = DarkGreen;
                        LedBoot.BackColor = DarkGreen;
                        break;
                }


                bool isDumperActive = tl866_report.DeviceCode.ToLower() == "codedump" &&
                                      tl866_report.SerialCode == "000000000000000000000000";

                if (isDumperActive)
                {
                    usb.Write(new[] {Firmware.DUMPER_INFO});
                    Dumper_Report dumper_report = new Dumper_Report();
                    if (usb.Read(dumper_report.buffer) > 0)
                    {
                        devcode = dumper_report.DeviceCode;
                        serial = dumper_report.SerialCode;
                    }
                    else
                    {
                        devcode = "";
                        serial = "";
                    }
                }
                else
                {
                    devcode = tl866_report.DeviceCode;
                    serial = tl866_report.SerialCode;
                }


                if (AdvancedWindow != null && !AdvancedWindow.IsDisposed)
                    AdvancedWindow.GetInfo();
                TxtInfo.AppendText(string.Format("Device code: {0}{1}\n", devcode,
                    firmware.Calc_CRC(devcode, serial) ? "(Bad device code)" : ""));
                TxtInfo.AppendText(string.Format("Serial number: {0}{1}\n", serial,
                    firmware.Calc_CRC(devcode, serial) ? "(Bad serial code)" : ""));
                TxtInfo.AppendText(string.Format("Firmware version: {0}",
                    isDumperActive
                        ? "Firmware dumper"
                        : tl866_report.Device_Status == (byte) Firmware.DEVICE_STATUS.NORMAL_MODE
                            ? string.Format("{0}.{1}.{2}", tl866_report.hardware_version,
                                tl866_report.firmware_version_major,
                                tl866_report.firmware_version_minor)
                            : "Bootloader"));
                BtnDump.Enabled = isDumperActive;
                BtnAdvanced.Enabled = isDumperActive;
            }
            else
            {
                BtnDump.Enabled = false;
                BtnAdvanced.Enabled = false;
                Leds_Off();
                TxtInfo.Clear();
                devcode = "";
                serial = "";
                if (AdvancedWindow != null && !AdvancedWindow.IsDisposed)
                    AdvancedWindow.GetInfo();
            }
        }


        protected override void WndProc(ref Message m)
        {
            usb.ProcessWindowsMessage(ref m);
            base.WndProc(ref m);
        }


        //Functions//////////////////////////////
        private void Reflash(int version)
        {
            TL866_Report tl866_report = new TL866_Report();
            Get_Report(tl866_report);

            if (tl866_report.Device_Status != (byte) Firmware.DEVICE_STATUS.BOOTLOADER_MODE)
            {
                Reset_Device();
                if (!wait_for_device())
                {
                    MessageBox.Show("Reset Error!", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
            }

            Thread.Sleep(1000);
            Message("<erasing...>");
            worker.ReportProgress((int) led_action.led_erase_on);
            if (!Erase_Device(firmware.GetEraseParametter(devtype)))
            {
                MessageBox.Show("Erase failed", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (worker.CancellationPending)
                return;
            worker.ReportProgress((int) led_action.led_erase_off);
            Thread.Sleep(1000);
            worker.ReportProgress((int) led_action.led_write_on);
            Message("<writing...>");
            if (!Write_Device(version == (int) Firmware.FIRMWARE_TYPE.FIRMWARE_CUSTOM
                ? firmware.Encrypt_Firmware(Resources.Dumper, devtype)
                : firmware.GetEncryptedFirmware(version, devtype)))
            {
                worker.ReportProgress((int) led_action.led_write_off);
                worker.ReportProgress((int) led_action.led_erase_off);
                MessageBox.Show("Reflash Failed!", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Thread.Sleep(500);
            Message("<resetting...>");
            worker.ReportProgress((int) led_action.led_write_off);
            worker.ReportProgress((int) led_action.led_erase_off);
            Reset_Device();
            if (!wait_for_device())
            {
                SetProgressBar(0);
                MessageBox.Show("Reset failed", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }


            Get_Report(tl866_report);
            if (tl866_report.Device_Status == (byte) Firmware.DEVICE_STATUS.NORMAL_MODE)
                MessageBox.Show("Reflash O.K.!", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show("Reflash Failed!", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetProgressBar(0);
        }

        private bool Write_Device(byte[] buffer)
        {
            if (!usb.OpenDevice(usb.Get_Devices()[0]))
                return false;
            byte[] b = new byte[Firmware.BLOCK_SIZE + 7];
            uint address = Firmware.BOOTLOADER_SIZE;
            SetProgressBar(0);
            for (int i = 0; i < buffer.Length; i += Firmware.BLOCK_SIZE)
            {
                if (worker.CancellationPending)
                    return false;
                b[0] = Firmware.WRITE_COMMAND;
                b[1] = 0;
                b[2] = Firmware.BLOCK_SIZE;
                b[3] = 0;
                Array.Copy(BitConverter.GetBytes(address), 0, b, 4, 4);
                Array.Copy(buffer, i, b, 7, Firmware.BLOCK_SIZE);
                address += Firmware.BLOCK_SIZE - 16;
                if (!usb.Write(b))
                    return false;
                SetProgressBar(i);
            }
            return true;
        }


        private void Get_Report(TL866_Report report)
        {
            usb.Write(new byte[] {Firmware.REPORT_COMMAND, 0, 0, 0, 0});
            usb.Read(report.buffer);
        }


        private bool Erase_Device(byte magic_number)
        {
            if (usb.DevicesCount > 0)
                if (usb.OpenDevice(usb.Get_Devices()[0]))
                {
                    byte[] buffer = new byte[20];
                    Array.Clear(buffer, 0, buffer.Length);
                    buffer[0] = Firmware.ERASE_COMMAND;
                    buffer[7] = magic_number;
                    usb.Write(buffer);
                    byte[] readbuffer = new byte[32];
                    usb.Read(readbuffer);
                    if (readbuffer[0] == Firmware.ERASE_COMMAND)
                        return true;
                }
            return false;
        }


        private void Leds_Off()
        {
            LedBoot.BackColor = DarkGreen;
            LedNorm.BackColor = DarkGreen;
            LedErase.BackColor = DarkYellow;
            LedWrite.BackColor = DarkRed;
        }


        public bool CheckDevices(Form parent)
        {
            if (usb.DevicesCount == 0)
            {
                MessageBox.Show(parent, Utils.NO_DEVICE, "TL866", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return false;
            }
            if (usb.DevicesCount > 1)
            {
                MessageBox.Show(parent, Utils.MULTIPLE_DEVICES, "TL866", MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation);
                return false;
            }
            return true;
        }


        private void SetProgressBar(int percentage)
        {
            try
            {
                if (ProgressBar1.InvokeRequired)
                {
                    SetProgressBarCallBack c = SetProgressBar;
                    Invoke(c, percentage);
                }
                else
                {
                    ProgressBar1.Value = percentage;
                }
            }
            catch
            {
            }
        }

        private void Message(string message)
        {
            try
            {
                if (ProgressBar1.InvokeRequired)
                {
                    SetMessageCallBack c = Message;
                    Invoke(c, message);
                }
                else
                {
                    string[] lines = TxtInfo.Lines;
                    if (lines.Length > 0)
                    {
                        lines[1] = "Device status: Bootloader mode " + message;
                        TxtInfo.Lines = lines;
                    }
                }
            }
            catch
            {
            }
        }


        private void Dump(string filepath)
        {
            byte[] buffer = new byte[Firmware.FLASH_SIZE]; //128Kbytes buffer   
            byte[] readbuffer = new byte[64];
            for (int i = 0; i < buffer.Length; i += 64)
            {
                if (worker.CancellationPending)
                    return;
                usb.Write(new byte[]
                {
                    Firmware.DUMPER_READ_FLASH, 64, (byte) (i & 0xFF), (byte) ((i >> 8) & 0xFF),
                    (byte) ((i >> 16) & 0xFF)
                });
                if (usb.Read(readbuffer) != 64)
                {
                    MessageBox.Show("Read error!", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
                Array.Copy(readbuffer, 0, buffer, i, readbuffer.Length);
                SetProgressBar(i);
                Application.DoEvents();
            }
            Array.Copy(firmware.GetUnencryptedFirmware(devtype), 0, buffer, Firmware.BOOTLOADER_SIZE,
                Firmware.UNENCRYPTED_FIRMWARE_SIZE);
            try
            {
                if (filepath.EndsWith(".hex"))
                {
                    HexWriter hexwriter = new HexWriter(File.CreateText(filepath));
                    hexwriter.WriteHex(buffer);
                }
                else
                {
                    File.WriteAllBytes(filepath, buffer);
                }
                MessageBox.Show("Firmware dump complete!", "TL866", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetProgressBar(0);
            }
            catch
            {
                SetProgressBar(0);
                MessageBox.Show("Error creating dump.hex file", "TL866", MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation);
            }
        }


        private void SaveHex(byte[] data)
        {
            ////Writing the main firmware or empty on choice
            if (!OptionFull.Checked)
                for (int i = Firmware.BOOTLOADER_SIZE; i < Firmware.XOR_TABLE_OFFSET; i++)
                    data[i] = 0xFF;
            ////Writing serial code
            byte[] info = new byte[Firmware.BLOCK_SIZE];
            Array.Copy(data, Firmware.SERIAL_OFFSET, info, 0, info.Length);
            firmware.DecryptSerial(info, data);
            string s1 = TxtDevcode.Text + new string(' ', Firmware.DEVCODE_LENGHT - TxtDevcode.Text.Length);
            string s2 = TxtSerial.Text + new string(' ', Firmware.SERIALCODE_LENGHT - TxtSerial.Text.Length);
            Array.Copy(Encoding.ASCII.GetBytes(s1), 0, info, 0, Firmware.DEVCODE_LENGHT);
            Array.Copy(Encoding.ASCII.GetBytes(s2), 0, info, Firmware.DEVCODE_LENGHT, Firmware.SERIALCODE_LENGHT);
            firmware.EncryptSerial(info, data);
            Array.Copy(info, 0, data, Firmware.SERIAL_OFFSET, info.Length);

            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Title = "Firmware output hex file";
            dlg.Filter = "hex files (*.hex)|*.hex|All files (*.*)|*.*";
            dlg.CheckPathExists = true;
            dlg.OverwritePrompt = true;
            if (dlg.ShowDialog() == DialogResult.OK)
                try
                {
                    if (File.Exists(dlg.FileName))
                        File.Delete(dlg.FileName);
                    if (dlg.FileName.EndsWith(".hex"))
                    {
                        HexWriter hexwriter = new HexWriter(File.CreateText(dlg.FileName));
                        hexwriter.WriteHex(data);
                    }
                    else
                    {
                        File.WriteAllBytes(dlg.FileName, data);
                    }
                    SystemSounds.Asterisk.Play();
                }
                catch
                {
                    MessageBox.Show(string.Format("Error creating file {0}", dlg.FileName), "TL866",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
        }


        //wait for device to reset
        private bool wait_for_device()
        {
            int cnt = 50; //5 seconds
            while (usb.DevicesCount > 0) //wait for device to leave
            {
                Thread.Sleep(100);
                if (!(--cnt > 0))
                    return false; //reset error
            }

            cnt = 50; //5 seconds
            while (!(usb.DevicesCount > 0)) //wait for device to arrive
            {
                Thread.Sleep(100);
                if (!(--cnt > 0))
                    return false; //reset error
            }
            return true; //device reset ok
        }


        private void Reset_Device()
        {
            reset_flag = true;
            usb.Write(new byte[] {Firmware.RESET_COMMAND, 0, 0, 0});
            usb.CloseDevice();
        }

        private enum device_action
        {
            reset_device,
            reflash_device,
            dump_device
        }


        private enum led_action
        {
            led_erase_on,
            led_erase_off,
            led_write_on,
            led_write_off
        }

        //Delegates
        private delegate void SetProgressBarCallBack(int percentage);

        private delegate void SetMessageCallBack(string message);
    }
}