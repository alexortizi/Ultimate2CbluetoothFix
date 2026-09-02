using System;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.DirectInput;
using BitDoFixer.Services;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;



namespace BitDoFixer
{
    public enum RemapperStatus { NotFound, Connected, Disconnected }

    internal static class BluetoothRemapper
{
    public static async Task RunAsync(IntPtr hwnd, CancellationToken token, Action<string>? logCallback = null, Action<RemapperStatus>? statusCallback = null)
    {
        void Log(string m) => logCallback?.Invoke(m);

        var loc = Localization.Instance;
        Log(loc.LogMapperStart);

        Joystick? joystick = null;
        ViGEmClient? client = null;
        Effect? forceFeedbackEffect = null;
        EffectParameters? effectParams = null;

        try
        {
            using var directInput = new DirectInput();

            var devices = directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly);
            if (devices.Count == 0) devices = directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly);

            if (devices.Count == 0)
            {
                Log(loc.LogMapperNotFound);
                statusCallback?.Invoke(RemapperStatus.NotFound);
                return;
            }

            var chosen = devices[0];
            Log(loc.LogMapperSource(chosen.InstanceName));

            joystick = new Joystick(directInput, chosen.InstanceGuid);
            joystick.SetCooperativeLevel(hwnd, CooperativeLevel.Exclusive | CooperativeLevel.Background);
            joystick.Properties.BufferSize = 128; // Buffer
            joystick.Acquire();

            try
            {
                var actuators = joystick.GetObjects(DeviceObjectTypeFlags.ForceFeedbackActuator)
                                        .Select(x => (int)x.ObjectId)
                                        .ToArray();

                if (actuators.Length > 0)
                {
                    // Tek aktüatör kullanıyoruz: çoğu DInput cihazında tek motor vardır,
                    // ve sıfır olmayan tek eksenli yön (Cartesian'da "1") en güvenilir/yaygın çalışan ayardır.
                    var axes = new[] { actuators[0] };
                    var directions = new[] { 1 };

                    effectParams = new EffectParameters
                    {
                        Flags = EffectFlags.Cartesian | EffectFlags.ObjectIds,
                        StartDelay = 0,
                        SamplePeriod = 0,
                        Duration = -1, // Infinite
                        TriggerButton = -1,
                        TriggerRepeatInterval = 0,
                        Axes = axes,
                        Directions = directions,
                        Envelope = null,
                        Parameters = new ConstantForce { Magnitude = 0 }
                    };

                    forceFeedbackEffect = new Effect(joystick, EffectGuid.ConstantForce, effectParams);
                    forceFeedbackEffect.Download();
                    Log("Vibration support (Force Feedback) enabled.");
                }
                else
                {
                    Log("This device does not report a Force Feedback actuator; rumble is unavailable.");
                }
            }
            catch (Exception ex)
            {
                Log($"Vibration setup failed: {ex.Message} (continuing without rumble)");
            }

            client = new ViGEmClient();
            var controller = client.CreateXbox360Controller(0x045E, 0x028E);

            controller.FeedbackReceived += (sender, args) =>
            {
                if (forceFeedbackEffect != null && effectParams != null)
                {
                    try
                    {
                        // Convert ViGEm motor values (0-255) to DInput Magnitude (-10000 to 10000)
                        int maxMotor = Math.Max(args.LargeMotor, args.SmallMotor);
                        int magnitude = (maxMotor * 10000) / 255;

                        effectParams.Parameters = new ConstantForce { Magnitude = magnitude };
                        forceFeedbackEffect.SetParameters(effectParams, EffectParameterFlags.TypeSpecificParameters);

                        if (magnitude > 0) forceFeedbackEffect.Start(1, EffectPlayFlags.NoDownload);
                        else forceFeedbackEffect.Stop();
                    }
                    catch { } // Ignore runtime FFB errors to avoid crashing the mapper
                }
            };

            controller.Connect();
            Log(loc.LogMapperReady);
            statusCallback?.Invoke(RemapperStatus.Connected);

            var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));

            while (await timer.WaitForNextTickAsync(token))
            {
                joystick.Poll();
                var state = joystick.GetCurrentState();
                if (state is null) continue;

                var buttons = state.Buttons;

                short lx = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.X));
                short ly = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.Y));
                short rx = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.Z));
                short ry = Xbox360Mapping.ApplyDeadzone(Xbox360Mapping.NormalizeAxis(state.RotationZ));

                controller.SetAxisValue(Xbox360Axis.LeftThumbX, lx);
                controller.SetAxisValue(Xbox360Axis.LeftThumbY, Xbox360Mapping.NegateAxis(ly));
                controller.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                controller.SetAxisValue(Xbox360Axis.RightThumbY, Xbox360Mapping.NegateAxis(ry));

                byte lt = 0; if (Xbox360Mapping.GetBtn(buttons, 8)) lt = 255;
                byte rt = 0; if (Xbox360Mapping.GetBtn(buttons, 9)) rt = 255;
                controller.SetSliderValue(Xbox360Slider.LeftTrigger, lt);
                controller.SetSliderValue(Xbox360Slider.RightTrigger, rt);

                SetButton(controller, Xbox360Button.A, Xbox360Mapping.GetBtn(buttons, 0));
                SetButton(controller, Xbox360Button.B, Xbox360Mapping.GetBtn(buttons, 1));
                SetButton(controller, Xbox360Button.X, Xbox360Mapping.GetBtn(buttons, 3));
                SetButton(controller, Xbox360Button.Y, Xbox360Mapping.GetBtn(buttons, 4));

                SetButton(controller, Xbox360Button.LeftShoulder, Xbox360Mapping.GetBtn(buttons, 6));
                SetButton(controller, Xbox360Button.RightShoulder, Xbox360Mapping.GetBtn(buttons, 7));

                SetButton(controller, Xbox360Button.Back, Xbox360Mapping.GetBtn(buttons, 10));
                SetButton(controller, Xbox360Button.Start, Xbox360Mapping.GetBtn(buttons, 11));

                SetButton(controller, Xbox360Button.LeftThumb, Xbox360Mapping.GetBtn(buttons, 13));
                SetButton(controller, Xbox360Button.RightThumb, Xbox360Mapping.GetBtn(buttons, 14));

                var dpad = Xbox360Mapping.PovToDpad(state.PointOfViewControllers);
                controller.SetButtonState(Xbox360Button.Up, dpad.Up);
                controller.SetButtonState(Xbox360Button.Right, dpad.Right);
                controller.SetButtonState(Xbox360Button.Down, dpad.Down);
                controller.SetButtonState(Xbox360Button.Left, dpad.Left);

                controller.SubmitReport();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
        catch (Exception ex)
        {
            Log(loc.LogMapperError(ex.Message));
            statusCallback?.Invoke(RemapperStatus.Disconnected);
        }
        finally
        {
            forceFeedbackEffect?.Dispose();
            joystick?.Dispose();
            client?.Dispose();
        }
    }

    private static void SetButton(IXbox360Controller c, Xbox360Button btn, bool pressed)
    {
        if (pressed) c.SetButtonState(btn, true);
        else c.SetButtonState(btn, false);
    }

    }
}
