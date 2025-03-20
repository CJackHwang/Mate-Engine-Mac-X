using System;
using UnityEditor.IMGUI.Controls;

namespace CitrioN.Common.Editor
{
  public class ClassDropdownItem : AdvancedDropdownItem
  {
    public Type Type { get; private set; }

    public ClassDropdownItem(string name, Type type) : base(name)
    {
      Type = type;
    }
  }
}