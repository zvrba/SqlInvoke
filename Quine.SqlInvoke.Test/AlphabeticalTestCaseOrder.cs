using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Quine.SqlInvoke.Test
{
    // Test cases change database state, so they have to be executed in a particular order.
    public class AlphabeticalTestCaseOrder : ITestCaseOrderer
    {
        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase {
            return testCases.OrderBy(x => x.TestMethod.Method.Name, StringComparer.OrdinalIgnoreCase);
        }
    }
}
