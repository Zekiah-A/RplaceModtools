using System;
using System.ComponentModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RplaceModtools.ViewModels;
using RplaceModtools.Views;

namespace RplaceModtools
{
    public class ViewLocator : IDataTemplate
    {
        public IControl Build(object data)
        {
            var name = data.GetType().FullName!
                .Replace("ViewModels", "Views")
                .Replace("ViewModel", "");
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control) Activator.CreateInstance(type)!;
            }
            return new TextBlock {Text = "Not Found: " + name};
        }

        public bool Match(object data)
        {
            return data is INotifyPropertyChanged;
        }
    }
}