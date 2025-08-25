using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace LiveCoding
{
    public class LiveCodingGHInfo : GH_AssemblyInfo
    {
        public override string Name => "LiveCodingGH";

        public override Bitmap Icon => null;

        public override string Description => "Live coding bridge for Grasshopper with WebSocket support and canvas analysis";

        public override Guid Id => new Guid("4A5F8E6B-6F2E-4F92-A3B5-6B1C7C0D5B42");

        public override string AuthorName => "LiveCoding Team";

        public override string AuthorContact => "";
    }
}