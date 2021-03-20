using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests
{
    [TestClass]
    public class ExpressionEvaluationUnitTests
    {
        LuaDkmDebuggerComponent.ExpressionEvaluation evaluation = new LuaDkmDebuggerComponent.ExpressionEvaluation(null, null, null, null, 0, null);

        [TestMethod]
        public void TestSimpleValues()
        {
            {
                var result = evaluation.Evaluate("nil");

                Assert.IsNotNull(result);

                Assert.AreEqual("nil", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("true");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("false");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }
        }

        [TestMethod]
        public void TestNumbers()
        {
            {
                var result = evaluation.Evaluate("1");

                Assert.IsNotNull(result);

                Assert.AreEqual("1", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("123456789");

                Assert.IsNotNull(result);

                Assert.AreEqual("123456789", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("0.125");

                Assert.IsNotNull(result);

                Assert.AreEqual("0.125", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("1e5");

                Assert.IsNotNull(result);

                Assert.AreEqual("100000", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("0xffe");

                Assert.IsNotNull(result);

                Assert.AreEqual("4094", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("0XBAAB");

                Assert.IsNotNull(result);

                Assert.AreEqual("47787", result.AsSimpleDisplayString(10));
            }
        }

        [TestMethod]
        public void TestUnary()
        {
            {
                var result = evaluation.Evaluate("-10");

                Assert.IsNotNull(result);

                Assert.AreEqual("-10", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("- 20");

                Assert.IsNotNull(result);

                Assert.AreEqual("-20", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("not false");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("not true");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("not nil");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("not 3");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }
        }

        [TestMethod]
        public void TestMultiplicative()
        {
            {
                var result = evaluation.Evaluate("2 * 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("8", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("2.5 * 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("10", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("10 / 2");

                Assert.IsNotNull(result);

                Assert.AreEqual("5", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("3 / 2");

                Assert.IsNotNull(result);

                Assert.AreEqual("1.5", result.AsSimpleDisplayString(10));
            }
        }

        [TestMethod]
        public void TestAdditive()
        {
            {
                var result = evaluation.Evaluate("2 + 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("6", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("5.5 - 1.5");

                Assert.IsNotNull(result);

                Assert.AreEqual("4", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("1.5 - 5.5");

                Assert.IsNotNull(result);

                Assert.AreEqual("-4", result.AsSimpleDisplayString(10));
            }
        }

        [TestMethod]
        public void TestConcatenation()
        {
            {
                var result = evaluation.Evaluate("2 .. 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("\"24\"", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"hello\"..2");

                Assert.IsNotNull(result);

                Assert.AreEqual("\"hello2\"", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"hello\"..\" world\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("\"hello world\"", result.AsSimpleDisplayString(10));
            }
        }

        [TestMethod]
        public void TestComparisons()
        {
            {
                var result = evaluation.Evaluate("2 > 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("4 > 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("8 > 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("2 >= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("4 >= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("8 >= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("2 < 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("4 < 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("8 < 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("2 <= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("4 <= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("8 <= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("2 == 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("4 == 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("2 ~= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("4 ~= 4");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"a\" > \"b\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"a\" < \"b\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"a\" >= \"b\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"b\" >= \"a\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"a\" >= \"a\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"me\" == \"me\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"me\" ~= \"me\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"me\" == \"you\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("\"me\" ~= \"you\"");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }
        }

        [TestMethod]
        public void TestLogical()
        {
            {
                var result = evaluation.Evaluate("(1 < 2) and (10 > 2)");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("(10 < 2) or (10 > 2)");

                Assert.IsNotNull(result);

                Assert.AreEqual("true", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("(10 < 2) and (10 > 2)");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("(1 < 2) and (10 > 20)");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }

            {
                var result = evaluation.Evaluate("(10 < 2) or (10 > 20)");

                Assert.IsNotNull(result);

                Assert.AreEqual("false", result.AsSimpleDisplayString(10));
            }
        }
    }
}
