using System;
using System.Windows.Controls;
using CncControlApp.Managers;

namespace CncControlApp.Tests   
{
    /// <summary>
    /// Simple test class for GCodeOverlayManager functionality
    /// Can be run manually or integrated with test framework
    /// Compatible with C# 7.3 and .NET Framework 4.8.1
    /// </summary>
    public class GCodeOverlayManagerTests
    {
        private Canvas _testCanvas;
        private Canvas _testOverlay;
        private GCodeOverlayManager _overlayManager;

        /// <summary>
        /// Initialize test environment
        /// </summary>
        public void Setup()
        {
            _testCanvas = new Canvas { Width = 800, Height = 600 };
            _testOverlay = new Canvas { Width = 800, Height = 600 };
            _overlayManager = new GCodeOverlayManager(_testCanvas, _testOverlay);
        }

        /// <summary>
        /// Test constructor initialization
        /// </summary>
        public bool Test_Constructor_ShouldInitializeCorrectly()
        {
            try
            {
                // Arrange & Act
                Setup();
                var manager = new GCodeOverlayManager(_testCanvas, _testOverlay);

                // Assert
                if (manager == null)
                {
                    Console.WriteLine("❌ FAIL: Manager is null");
                    return false;
                }

                if (manager.WorkspaceLimitsLoaded != false) // Initially false
                {
                    Console.WriteLine($"❌ FAIL: Expected WorkspaceLimitsLoaded=false, got {manager.WorkspaceLimitsLoaded}");
                    return false;
                }

                Console.WriteLine("✅ PASS: Constructor test passed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FAIL: Constructor test failed with exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test part dimensions update
        /// </summary>
        public bool Test_UpdatePartDimensions_ShouldUpdateValues()
        {
            try
            {
                // Arrange
                Setup();
                double expectedX = 100.5;
                double expectedY = 200.3;

                // Act
                _overlayManager.UpdatePartDimensions(expectedX, expectedY);

                // Assert
                if (Math.Abs(_overlayManager.LastXRange - expectedX) > 0.001)
                {
                    Console.WriteLine($"❌ FAIL: Expected X={expectedX}, got {_overlayManager.LastXRange}");
                    return false;
                }

                if (Math.Abs(_overlayManager.LastYRange - expectedY) > 0.001)
                {
                    Console.WriteLine($"❌ FAIL: Expected Y={expectedY}, got {_overlayManager.LastYRange}");
                    return false;
                }

                Console.WriteLine("✅ PASS: UpdatePartDimensions test passed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FAIL: UpdatePartDimensions test failed with exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test overlay clearing
        /// </summary>
        public bool Test_ClearOverlay_ShouldRemoveAllChildren()
        {
            try
            {
                // Arrange
                Setup();
                _testOverlay.Children.Add(new TextBlock { Text = "Test" });
                
                if (_testOverlay.Children.Count != 1)
                {
                    Console.WriteLine($"❌ FAIL: Expected 1 child, got {_testOverlay.Children.Count}");
                    return false;
                }

                // Act
                _overlayManager.ClearOverlay();

                // Assert
                if (_testOverlay.Children.Count != 0)
                {
                    Console.WriteLine($"❌ FAIL: Expected 0 children after clear, got {_testOverlay.Children.Count}");
                    return false;
                }

                Console.WriteLine("✅ PASS: ClearOverlay test passed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FAIL: ClearOverlay test failed with exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test constructor with null canvas
        /// </summary>
        public bool Test_Constructor_WithNullCanvas_ShouldThrowException()
        {
            try
            {
                // Act & Assert
                try
                {
                    new GCodeOverlayManager(null, _testOverlay);
                    Console.WriteLine("❌ FAIL: Expected ArgumentNullException was not thrown");
                    return false;
                }
                catch (ArgumentNullException)
                {
                    Console.WriteLine("✅ PASS: Constructor null test passed - ArgumentNullException thrown as expected");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ FAIL: Expected ArgumentNullException, got {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FAIL: Constructor null test failed with unexpected exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test data structure for C# 7.3 compatibility
        /// </summary>
        public class TestInfo
        {
            public string Name { get; set; }
            public Func<bool> Test { get; set; }

            public TestInfo(string name, Func<bool> test)
            {
                Name = name;
                Test = test;
            }
        }

        /// <summary>
        /// Run all tests - C# 7.3 compatible version
        /// </summary>
        public void RunAllTests()
        {
            Console.WriteLine("=== GCodeOverlayManager Tests ===");
            Console.WriteLine();

            // C# 7.3 compatible approach - using class instead of tuple
            var tests = new TestInfo[]
            {
                new TestInfo("Constructor Initialization", Test_Constructor_ShouldInitializeCorrectly),
                new TestInfo("Update Part Dimensions", Test_UpdatePartDimensions_ShouldUpdateValues),
                new TestInfo("Clear Overlay", Test_ClearOverlay_ShouldRemoveAllChildren),
                new TestInfo("Constructor Null Check", Test_Constructor_WithNullCanvas_ShouldThrowException)
            };

            int passed = 0;
            int total = tests.Length;

            foreach (var testInfo in tests)
            {
                Console.WriteLine($"Running: {testInfo.Name}");
                try
                {
                    if (testInfo.Test())
                    {
                        passed++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ FAIL: {testInfo.Name} - Unhandled exception: {ex.Message}");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"=== Test Results: {passed}/{total} passed ===");
            
            if (passed == total)
            {
                Console.WriteLine("🎉 All tests passed!");
            }
            else
            {
                Console.WriteLine($"⚠️ {total - passed} test(s) failed");
            }
        }

        /// <summary>
        /// Static method to run tests from anywhere
        /// </summary>
        public static void RunTests()
        {
            var testInstance = new GCodeOverlayManagerTests();
            testInstance.RunAllTests();
        }
    }

    /// <summary>
    /// Test runner utility - can be called from debug console or main application
    /// </summary>
    public static class TestRunner
    {
        /// <summary>
        /// Run GCodeOverlayManager tests
        /// </summary>
        public static void RunGCodeOverlayTests()
        {
            try
            {
                Console.WriteLine("Starting GCodeOverlayManager tests...");
                GCodeOverlayManagerTests.RunTests();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test runner failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Run all available tests
        /// </summary>
        public static void RunAllTests()
        {
            Console.WriteLine("=== CNC Control App Test Suite ===");
            Console.WriteLine();
            
            RunGCodeOverlayTests();
            
            Console.WriteLine();
            Console.WriteLine("=== Test Suite Complete ===");
        }

        /// <summary>
        /// Run tests and show results in a message box (WPF compatible)
        /// </summary>
        public static void RunTestsWithMessageBox()
        {
            try
            {
                // Capture console output
                var originalOut = Console.Out;
                var stringWriter = new System.IO.StringWriter();
                Console.SetOut(stringWriter);

                // Run tests
                RunAllTests();

                // Restore console output
                Console.SetOut(originalOut);

                // Show results in message box
                string results = stringWriter.ToString();
                System.Windows.MessageBox.Show(results, "Test Results", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Information);

                // Also write to debug output
                System.Diagnostics.Debug.WriteLine("=== Test Results ===");
                System.Diagnostics.Debug.WriteLine(results);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Test execution failed: {ex.Message}", "Test Error", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}