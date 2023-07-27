using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace RplaceModtools
{
    public class ViewLocator : IDataTemplate
    {
        public Control Build(object? data) // TODO: Was IControl
        {
            if (data is null)
            {
                return new TextBlock {Text = "Not Found: data was null"};
            }
            
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

        public bool Match(object? data)
        {
            return data is INotifyPropertyChanged;
        }
    }
}