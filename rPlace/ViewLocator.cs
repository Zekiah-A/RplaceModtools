using System;
using System.ComponentModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using rPlace.ViewModels;
using rPlace.Views;

namespace rPlace
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