using System;
using System.Threading;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App.Plugins.JavaScript;

namespace PCL.Core.Test.App.Plugins;

[TestClass]
public class JavaScriptRawObjectTest
{
    [TestMethod]
    public void RawObject_ShouldControlWpfElementExplicitly()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var engine = new JavaScriptRuntime();
                var button = new Button { Name = "TestButton", Content = "Before" };
                var element = new JavaScriptElement(button, engine);

                Assert.AreEqual("Button", element.raw.typeName);
                Assert.IsTrue(element.raw.properties().Length > 0);

                element.raw.set("Content", "After");
                Assert.AreEqual("After", button.Content);

                element.raw.setDp("Height", 54d);
                Assert.AreEqual(54d, button.Height);

                element.raw.call("Focus");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
    }
}