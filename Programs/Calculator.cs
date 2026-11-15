using System;
using System.IO;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Windose;
using Windose.Programs;

namespace Windose.Programs
{
    /// <summary>
    /// Classic Windows 7 Calculator applet with full keyboard and mouse support.
    /// </summary>
    public class Calculator : Window
    {
        private double accumulator;
        private string currentInput = "0";
        private string pendingOp = "";
        private bool startNewNumber = true;
        private readonly Label displayLabel;

        public Calculator(int x = 200, int y = 140)
            : base(x, y, 244, 320, "Calculator", true)
        {
            canResize = false;
            canMaximize = false;

            if (Palette.GlassEnabled)
            {
                useGlass = true;
            }

            // Display readout
            displayLabel = new Label(16, 34, 212, 36)
            {
                text = "0",
                fontSize = 20,
                textColor = Palette.ControlBlack,
                horizontalTextAlignment = HorizontalAlignment.Right,
                verticalAlignment = VerticalAlignment.Center,
                useBackground = false,
                clampSize = false
            };
            AddChild(displayLabel);

            // Grid buttons definition
            string[,] buttons = new string[,]
            {
                { "<-", "CE", "C", "+/-" },
                { "7",  "8",  "9", "/" },
                { "4",  "5",  "6", "*" },
                { "1",  "2",  "3", "-" },
                { "0",  ".",  "=", "+" }
            };

            int startX = 12;
            int startY = 76;
            int btnW = 48;
            int btnH = 32;
            int gapX = 6;
            int gapY = 5;

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    string text = buttons[r, c];
                    int bx = startX + c * (btnW + gapX);
                    int by = startY + r * (btnH + gapY);

                    Button btn = new Button(text, bx, by, btnW, btnH);
                    string captureText = text;
                    btn.leftClickAction = () => HandleButton(captureText);
                    AddChild(btn);
                }
            }
        }

        public override void DrawLocal()
        {
            base.DrawLocal();

            // Display screen sunken container
            DrawGradientVertical(Color.FromArgb(250, 252, 255), Color.FromArgb(235, 240, 245), 12, 12, 220, 44);
            DrawSunkenRectangle(12, 12, 220, 44);
        }

        private void HandleButton(string text)
        {
            switch (text)
            {
                case "0":
                case "1":
                case "2":
                case "3":
                case "4":
                case "5":
                case "6":
                case "7":
                case "8":
                case "9":
                    InputDigit(text);
                    break;

                case ".":
                    InputDecimal();
                    break;

                case "+/-":
                    Negate();
                    break;

                case "<-":
                    Backspace();
                    break;

                case "CE":
                    currentInput = "0";
                    startNewNumber = true;
                    UpdateDisplay();
                    break;

                case "C":
                    currentInput = "0";
                    accumulator = 0;
                    pendingOp = "";
                    startNewNumber = true;
                    UpdateDisplay();
                    break;

                case "+":
                case "-":
                case "*":
                case "/":
                    SetOperation(text);
                    break;

                case "=":
                    ComputeResult();
                    break;
            }
        }

        private void InputDigit(string digit)
        {
            if (startNewNumber || currentInput == "0")
            {
                currentInput = digit;
                startNewNumber = false;
            }
            else
            {
                if (currentInput.Length < 16)
                    currentInput += digit;
            }
            UpdateDisplay();
        }

        private void InputDecimal()
        {
            if (startNewNumber)
            {
                currentInput = "0.";
                startNewNumber = false;
            }
            else if (!currentInput.Contains("."))
            {
                currentInput += ".";
            }
            UpdateDisplay();
        }

        private void Negate()
        {
            if (currentInput != "0" && currentInput != "0.")
            {
                if (currentInput.StartsWith("-"))
                    currentInput = currentInput.Substring(1);
                else
                    currentInput = "-" + currentInput;
                UpdateDisplay();
            }
        }

        private void Backspace()
        {
            if (!startNewNumber && currentInput.Length > 0)
            {
                currentInput = currentInput.Substring(0, currentInput.Length - 1);
                if (currentInput == "" || currentInput == "-")
                {
                    currentInput = "0";
                    startNewNumber = true;
                }
                UpdateDisplay();
            }
        }

        private void SetOperation(string op)
        {
            if (!string.IsNullOrEmpty(pendingOp) && !startNewNumber)
            {
                ComputeResult();
            }
            else
            {
                double.TryParse(currentInput, out accumulator);
            }
            pendingOp = op;
            startNewNumber = true;
        }

        private void ComputeResult()
        {
            if (string.IsNullOrEmpty(pendingOp)) return;

            if (double.TryParse(currentInput, out double val))
            {
                double result = accumulator;
                switch (pendingOp)
                {
                    case "+": result = accumulator + val; break;
                    case "-": result = accumulator - val; break;
                    case "*": result = accumulator * val; break;
                    case "/":
                        if (val == 0)
                        {
                            currentInput = "Cannot divide by zero";
                            pendingOp = "";
                            startNewNumber = true;
                            UpdateDisplay();
                            return;
                        }
                        result = accumulator / val;
                        break;
                }

                currentInput = FormatResult(result);
                accumulator = result;
                pendingOp = "";
                startNewNumber = true;
                UpdateDisplay();
            }
        }

        private string FormatResult(double val)
        {
            string s = val.ToString("G12");
            if (s.Contains(".") && s.EndsWith("0"))
                s = s.TrimEnd('0').TrimEnd('.');
            return s;
        }

        private void UpdateDisplay()
        {
            displayLabel.text = currentInput;
            displayLabel.MarkDirty();
            MarkDirty();
        }

        public override void HandleKeyboard(KeyEvent keyEvent)
        {
            base.HandleKeyboard(keyEvent);

            if (keyEvent.Key == Key.Enter)
            {
                ComputeResult();
                return;
            }
            if (keyEvent.Key == Key.Backspace)
            {
                Backspace();
                return;
            }
            if (keyEvent.Key == Key.Escape)
            {
                HandleButton("C");
                return;
            }

            char c = GetPrintableCharacter(keyEvent);
            if (c >= '0' && c <= '9')
            {
                InputDigit(c.ToString());
            }
            else if (c == '.')
            {
                InputDecimal();
            }
            else if (c == '+' || c == '-' || c == '*' || c == '/')
            {
                SetOperation(c.ToString());
            }
            else if (c == '=')
            {
                ComputeResult();
            }
        }

        public override string GetComponentName() => "Calculator";
    }
}
