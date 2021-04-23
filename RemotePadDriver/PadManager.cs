using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace RemotePadDriver
{
    class PadManager
    {
        private static PadManager padManager;
        private Dispatcher dispatcher;

        private ViGEmClient vigem = new ViGEmClient();

        private ObservableCollection<PadObj> padList = new ObservableCollection<PadObj>();

        private PadManager() { }

        public Dispatcher Dispatcher { get => dispatcher; set => dispatcher = value; }
        internal ObservableCollection<PadObj> PadList { get => padList; set => padList = value; }

        public static PadManager GetInstance()
        {
            if (padManager == null)
            {
                padManager = new PadManager();
            }
            return padManager;
        }

        public void SwitchType(string id, PadType type)
        {
            PadObj padObj = Contains(id);
            if (padObj == null)
                return;
            if (padObj.Pad != null && padObj.Ready)
            {
                padObj.Ready = false;
                try
                {
                    padObj.Pad.Disconnect();
                }
                catch (VigemTargetNotPluggedInException e)
                {
                    Console.WriteLine(e.Message);
                }

            }
            switch (type)
            {
                case PadType.Xbox360:
                    padObj.Pad = vigem.CreateXbox360Controller();
                    padObj.Type = "Xbox360";
                    break;
                case PadType.Ds4:
                    padObj.Pad = vigem.CreateDualShock4Controller();
                    padObj.Type = "ds4";
                    break;
            }
            padObj.Pad.Connect();
            padObj.Ready = true;
        }

        public void FromServer(string id)
        {
            PadObj padObj = Contains(id);
            if (padObj != null)
                padObj.Socket = null;
        }

        public void Add(string id, Socket socket)
        {
            PadObj padObj = new PadObj() {
                Id = id,
                LastHB = Util.GetTime(),
                Socket = socket,
            };
            Dispatcher.BeginInvoke(new Action(() =>
            {
                padList.Add(padObj);
            }));
        }

        public void Remove(string id)
        {
            PadObj padObj = Contains(id);
            if (padObj == null)
                return;
            Remove(padObj);
        }

        public void Remove(PadObj padObj)
        {
            if (padObj.Socket != null)
                padObj.Socket.Close();
            if (padObj.Pad != null)
                padObj.Pad.Disconnect();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                padList.Remove(padObj);
            }));
        }

        public PadObj Contains(string id)
        {
            foreach (PadObj padObj in padList)
            {
                if (padObj.Id == id)
                    return padObj;
            }
            return null;
        }

        public void UpdateHB(string id, long time, double delay)
        {
            PadObj padObj = Contains(id);
            if (padObj == null)
                return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                padObj.LastHB = time;
                padObj.Delay = delay + "ms";
            }));
        }

        public void ProcBtn(Data protoData)
        {
            //await Task.Run(new Action(() =>
            //{
                PadObj padObj = Contains(protoData.Id);
                if (padObj == null)
                    return;
                if (padObj.Pad == null)
                    return;
                if (!padObj.Ready)
                    return;
                IXbox360Controller xboxController = null;
                IDualShock4Controller ds4Controller = null;
                if (padObj.Pad is IXbox360Controller)
                {
                    xboxController = (IXbox360Controller)padObj.Pad;
                }
                else if (padObj.Pad is IDualShock4Controller)
                {
                    ds4Controller = (IDualShock4Controller)padObj.Pad;
                }
                if (xboxController == null && ds4Controller == null)
                {
                    return;
                }

                switch (protoData.PadData.BtnType)
                {
                    case PadBtn.A:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.A, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.Cross, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.B:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.B, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.Circle, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.X:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.X, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.Square, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Y:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Y, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.Triangle, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Lb:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.LeftShoulder, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.ShoulderLeft, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Rb:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.RightShoulder, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.ShoulderRight, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.L3:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.LeftThumb, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.ThumbLeft, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.R3:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.RightThumb, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.ThumbRight, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Start:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Start, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.Options, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Select:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Back, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4Button.Share, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Xbox:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Guide, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            ds4Controller.SetButtonState(DualShock4SpecialButton.Ps, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.L2:
                        if (xboxController != null)
                        {
                            xboxController.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)protoData.PadData.BtnVal);
                        }
                        else
                        {
                            ds4Controller.SetSliderValue(DualShock4Slider.LeftTrigger, (byte)protoData.PadData.BtnVal);
                        }
                        break;
                    case PadBtn.R2:
                        if (xboxController != null)
                        {
                            xboxController.SetSliderValue(Xbox360Slider.RightTrigger, (byte)protoData.PadData.BtnVal);
                        }
                        else
                        {
                            ds4Controller.SetSliderValue(DualShock4Slider.RightTrigger, (byte)protoData.PadData.BtnVal);
                        }
                        break;
                    case PadBtn.Lax:
                        if (xboxController != null)
                        {
                            xboxController.SetAxisValue(Xbox360Axis.LeftThumbX, (short)protoData.PadData.BtnVal);
                        }
                        else
                        {
                            ds4Controller.SetAxisValue(DualShock4Axis.LeftThumbX, CalcDS4AxisVal((short)protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Lay:
                        if (xboxController != null)
                        {
                            xboxController.SetAxisValue(Xbox360Axis.LeftThumbY, (short)protoData.PadData.BtnVal);
                        }
                        else
                        {
                            ds4Controller.SetAxisValue(DualShock4Axis.LeftThumbY, (byte)(255 - CalcDS4AxisVal((short)protoData.PadData.BtnVal)));
                        }
                        break;
                    case PadBtn.Rax:
                        if (xboxController != null)
                        {
                            xboxController.SetAxisValue(Xbox360Axis.RightThumbX, (short)protoData.PadData.BtnVal);
                        }
                        else
                        {
                            ds4Controller.SetAxisValue(DualShock4Axis.RightThumbX, CalcDS4AxisVal((short)protoData.PadData.BtnVal));
                        }
                        break;
                    case PadBtn.Ray:
                        if (xboxController != null)
                        {
                            xboxController.SetAxisValue(Xbox360Axis.RightThumbY, (short)protoData.PadData.BtnVal);
                        }
                        else
                        {
                            ds4Controller.SetAxisValue(DualShock4Axis.RightThumbY, (byte)(255 - CalcDS4AxisVal((short)protoData.PadData.BtnVal)));
                        }
                        break;
                    case PadBtn.Dup:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Up, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            if (Convert.ToBoolean(protoData.PadData.BtnVal))
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.North);
                            }
                            else
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.None);
                            }
                        }
                        break;
                    case PadBtn.Ddown:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Down, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            if (Convert.ToBoolean(protoData.PadData.BtnVal))
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.South);
                            }
                            else
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.None);
                            }
                        }
                        break;
                    case PadBtn.Dleft:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Left, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            if (Convert.ToBoolean(protoData.PadData.BtnVal))
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.West);
                            }
                            else
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.None);
                            }
                        }
                        break;
                    case PadBtn.Dright:
                        if (xboxController != null)
                        {
                            xboxController.SetButtonState(Xbox360Button.Right, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        else
                        {
                            if (Convert.ToBoolean(protoData.PadData.BtnVal))
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.East);
                            }
                            else
                            {
                                ds4Controller.SetDPadDirection(DualShock4DPadDirection.None);
                            }
                        }
                        break;
                    case PadBtn.Ds4TouchPad:
                        if (ds4Controller != null)
                        {
                            ds4Controller.SetButtonState(DualShock4SpecialButton.Touchpad, Convert.ToBoolean(protoData.PadData.BtnVal));
                        }
                        break;
                }
            //}));
        }

        private short CalcAxisVal(int rate, int left, bool positive)
        {
            int val = rate * 255 + left;
            if (!positive)
            {
                val = -val;
            }
            if (val == 32768)
            {
                val = 32767;
            }
            return (short)val;
        }

        private byte CalcDS4AxisVal(int val)
        {
            if (val <= -32768)
            {
                val = -32767;
            }
            val = val + 32768;
            return (byte)(val / 257);
        }
    }
}
