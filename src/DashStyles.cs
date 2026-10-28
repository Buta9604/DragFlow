using System;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace DragFlow
{
    /// <summary>
    /// Looks for the standard controls (drop-downs, text boxes, check boxes, lists, scroll bars) in the dashboard's
    /// colours, so light and dark mode look the same everywhere. Written as XAML and read when the window opens.
    /// </summary>
    public static class DashStyles
    {
        static string C(Brush b)
        {
            var s = b as SolidColorBrush;
            return s == null ? "#FF808080" : s.Color.ToString();
        }

        public static ResourceDictionary Load(DashTheme t)
        {
            string xaml = Template
                .Replace("{TEXT}", C(t.Text)).Replace("{SUB}", C(t.Sub)).Replace("{FIELD}", C(t.Field)).Replace("{CARD}", C(t.Card))
                .Replace("{BORDER}", C(t.CardBorder)).Replace("{ACCENT}", C(t.Accent)).Replace("{ONACCENT}", C(t.OnAccent))
                .Replace("{HOVER}", C(t.Hover)).Replace("{SELECTED}", C(t.Selected));
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        const string Template = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='12'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Track x:Name='PART_Track' IsDirectionReversed='True'>
          <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
          <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
          <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{SUB}' Opacity='0.45' CornerRadius='4' Margin='3,2,3,2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        </Track>
      </ControlTemplate>
    </Setter.Value></Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='Height' Value='12'/>
        <Setter Property='Template'><Setter.Value>
          <ControlTemplate TargetType='ScrollBar'>
            <Track x:Name='PART_Track' IsDirectionReversed='False'>
              <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageLeftCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageRightCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
              <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{SUB}' Opacity='0.45' CornerRadius='4' Margin='2,3,2,3'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track>
          </ControlTemplate>
        </Setter.Value></Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='Background' Value='{FIELD}'/>
    <Setter Property='BorderBrush' Value='{BORDER}'/>
    <Setter Property='CaretBrush' Value='{TEXT}'/>
    <Setter Property='SelectionBrush' Value='{ACCENT}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='TextBox'>
        <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1,1,1,2' CornerRadius='4'>
          <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' Focusable='False'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{ACCENT}'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>

  <Style TargetType='PasswordBox'>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='Background' Value='{FIELD}'/>
    <Setter Property='BorderBrush' Value='{BORDER}'/>
    <Setter Property='CaretBrush' Value='{TEXT}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='PasswordBox'>
        <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1,1,1,2' CornerRadius='4'>
          <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' Focusable='False'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{ACCENT}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='CheckBox'>
        <StackPanel Orientation='Horizontal' Background='Transparent'>
          <Border x:Name='box' Width='18' Height='18' CornerRadius='4' BorderThickness='1' BorderBrush='{SUB}' Background='{FIELD}' VerticalAlignment='Center'>
            <Path x:Name='tick' Data='M3,9 L7,13 L15,4' Stroke='{ONACCENT}' StrokeThickness='2' Visibility='Collapsed'/>
          </Border>
          <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/>
        </StackPanel>
        <ControlTemplate.Triggers>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='box' Property='Background' Value='{ACCENT}'/>
            <Setter TargetName='box' Property='BorderBrush' Value='{ACCENT}'/>
            <Setter TargetName='tick' Property='Visibility' Value='Visible'/>
          </Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderThickness' Value='2'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>

  <Style TargetType='ListBox'>
    <Setter Property='Background' Value='{FIELD}'/>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='BorderBrush' Value='{BORDER}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ListBox'>
        <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='4' Padding='2'>
          <ScrollViewer Focusable='False'><ItemsPresenter/></ScrollViewer>
        </Border>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Rows' TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
        <Border x:Name='bd' Background='Transparent' CornerRadius='3' Padding='8,4,8,4'><ContentPresenter/></Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{HOVER}'/></Trigger>
          <Trigger Property='IsSelected' Value='True'><Setter TargetName='bd' Property='Background' Value='{SELECTED}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>

  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ComboBoxItem'>
        <Border x:Name='bd' Background='Transparent' CornerRadius='3' Padding='8,5,8,5'><ContentPresenter/></Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='{HOVER}'/></Trigger>
          <Trigger Property='IsSelected' Value='True'><Setter TargetName='bd' Property='Background' Value='{SELECTED}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>

  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='Background' Value='{FIELD}'/>
    <Setter Property='BorderBrush' Value='{BORDER}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ComboBox'>
        <Grid>
          <ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
            <ToggleButton.Template>
              <ControlTemplate TargetType='ToggleButton'>
                <Border x:Name='bd' Background='{FIELD}' BorderBrush='{BORDER}' BorderThickness='1,1,1,2' CornerRadius='4'>
                  <Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0' Data='M0,0 L4,4 L8,0' Stroke='{SUB}' StrokeThickness='1.5'/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{SUB}'/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter x:Name='shown' IsHitTestVisible='False' Margin='9,5,28,6' VerticalAlignment='Center'
                            Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>
          <TextBox x:Name='PART_EditableTextBox' Visibility='Collapsed' Margin='1,1,26,1' Padding='6,3,4,3' Background='Transparent' BorderBrush='Transparent'/>
          <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
            <Border Background='{CARD}' BorderBrush='{BORDER}' BorderThickness='1' CornerRadius='6' Padding='4' Margin='0,2,0,0'
                    MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='340'>
              <ScrollViewer><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsEditable' Value='True'>
            <Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/>
            <Setter TargetName='shown' Property='Visibility' Value='Hidden'/>
          </Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='{CARD}'/>
    <Setter Property='Foreground' Value='{TEXT}'/>
    <Setter Property='BorderBrush' Value='{BORDER}'/>
  </Style>
</ResourceDictionary>";
    }
}
