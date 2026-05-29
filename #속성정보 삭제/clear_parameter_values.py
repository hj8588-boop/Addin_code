import clr

clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import ElementId, StorageType

clr.AddReference("RevitAPIUI")
from Autodesk.Revit.UI import Selection
from Autodesk.Revit.Exceptions import OperationCanceledException

clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

clr.AddReference("System.Windows.Forms")
clr.AddReference("System.Drawing")
from System.Windows.Forms import (
    AnchorStyles,
    Button,
    CheckedListBox,
    DialogResult,
    Form,
    FormBorderStyle,
    Label,
    TextBox,
)
from System.Drawing import Point, Size

doc = DocumentManager.Instance.CurrentDBDocument
uidoc = DocumentManager.Instance.CurrentUIApplication.ActiveUIDocument

filter_text_input = IN[0] if len(IN) > 0 else ""
repick_input = IN[1] if len(IN) > 1 else True
execute_input = IN[2] if len(IN) > 2 else False


def normalize_text(value):
    if value is None:
        return ""
    return str(value).strip()


def to_bool(value):
    if isinstance(value, bool):
        return value
    return normalize_text(value).lower() in ["true", "1", "yes", "y"]


def pick_elements():
    refs = uidoc.Selection.PickObjects(
        Selection.ObjectType.Element,
        "Select elements whose parameter values will be cleared."
    )
    return [doc.GetElement(reference.ElementId) for reference in refs]


def get_cached_elements():
    try:
        cached_ids = _CACHED_SELECTION_IDS
    except Exception:
        cached_ids = []

    elements = []
    refreshed_ids = []

    for cached_id in cached_ids:
        element_id = cached_id if hasattr(cached_id, "IntegerValue") else ElementId(int(cached_id))
        element = doc.GetElement(element_id)
        if element is None:
            continue
        elements.append(element)
        refreshed_ids.append(element.Id.IntegerValue)

    globals()["_CACHED_SELECTION_IDS"] = refreshed_ids
    return elements


def cache_elements(elements):
    globals()["_CACHED_SELECTION_IDS"] = [
        element.Id.IntegerValue for element in elements if element is not None
    ]


def get_element_parameter_names(elements):
    names = []
    seen = set()

    for element in elements:
        if element is None:
            continue

        try:
            parameters = element.Parameters
        except Exception:
            parameters = []

        for parameter in parameters:
            try:
                name = normalize_text(parameter.Definition.Name)
            except Exception:
                name = ""

            if not name:
                continue

            lowered = name.lower()
            if lowered in seen:
                continue

            seen.add(lowered)
            names.append(name)

    names.sort(key=lambda item: item.lower())
    return names


def filter_parameter_names(names, filter_text):
    safe_filter = normalize_text(filter_text).lower()
    if not safe_filter:
        return list(names)
    return [name for name in names if safe_filter in name.lower()]


def show_parameter_picker(parameter_names, preselected_names, initial_filter):
    form = Form()
    form.Text = "Select Parameters To Clear"
    form.Width = 520
    form.Height = 640
    form.StartPosition = 1
    form.FormBorderStyle = FormBorderStyle.FixedDialog
    form.MinimizeBox = False
    form.MaximizeBox = False
    form.TopMost = True

    info_label = Label()
    info_label.Text = "Choose one or more parameter names found on the selected elements."
    info_label.Location = Point(12, 12)
    info_label.Size = Size(480, 36)
    form.Controls.Add(info_label)

    filter_label = Label()
    filter_label.Text = "Filter"
    filter_label.Location = Point(12, 54)
    filter_label.Size = Size(60, 20)
    form.Controls.Add(filter_label)

    filter_box = TextBox()
    filter_box.Location = Point(12, 76)
    filter_box.Size = Size(480, 24)
    filter_box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
    filter_box.Text = normalize_text(initial_filter)
    form.Controls.Add(filter_box)

    checklist = CheckedListBox()
    checklist.Location = Point(12, 110)
    checklist.Size = Size(480, 430)
    checklist.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
    checklist.CheckOnClick = True
    form.Controls.Add(checklist)

    ok_button = Button()
    ok_button.Text = "OK"
    ok_button.Location = Point(316, 555)
    ok_button.Size = Size(84, 30)
    ok_button.DialogResult = DialogResult.OK
    form.Controls.Add(ok_button)

    cancel_button = Button()
    cancel_button.Text = "Cancel"
    cancel_button.Location = Point(408, 555)
    cancel_button.Size = Size(84, 30)
    cancel_button.DialogResult = DialogResult.Cancel
    form.Controls.Add(cancel_button)

    form.AcceptButton = ok_button
    form.CancelButton = cancel_button

    selected_lookup = set([normalize_text(name).lower() for name in preselected_names])

    def refresh_list(*args):
        current_checked = set()
        for checked_item in checklist.CheckedItems:
            current_checked.add(normalize_text(checked_item).lower())

        checklist.Items.Clear()

        visible_names = filter_parameter_names(parameter_names, filter_box.Text)
        for name in visible_names:
            index = checklist.Items.Add(name)
            lowered = name.lower()
            should_check = lowered in selected_lookup or lowered in current_checked
            checklist.SetItemChecked(index, should_check)

    def on_text_changed(sender, args):
        refresh_list()

    filter_box.TextChanged += on_text_changed
    refresh_list()

    if form.ShowDialog() != DialogResult.OK:
        return None

    selected_names = []
    for checked_item in checklist.CheckedItems:
        selected_names.append(normalize_text(checked_item))

    deduped = []
    seen = set()
    for name in selected_names:
        lowered = name.lower()
        if lowered in seen:
            continue
        seen.add(lowered)
        deduped.append(name)

    return deduped


def clear_parameter_value(parameter):
    if parameter is None:
        return False, "Parameter not found."

    if parameter.IsReadOnly:
        return False, "Parameter is read-only."

    try:
        storage_type = parameter.StorageType

        if storage_type == StorageType.String:
            parameter.Set("")
            return True, "Cleared string."

        if storage_type == StorageType.Integer:
            parameter.Set(0)
            return True, "Reset integer to 0."

        if storage_type == StorageType.Double:
            parameter.Set(0.0)
            return True, "Reset number to 0."

        if storage_type == StorageType.ElementId:
            parameter.Set(ElementId.InvalidElementId)
            return True, "Reset ElementId."

        return False, "Unsupported storage type."
    except Exception as exc:
        return False, str(exc)


filter_text = normalize_text(filter_text_input)
repick = to_bool(repick_input)
execute = to_bool(execute_input)

selected_elements = []
selection_was_picked = False

try:
    if repick:
        selected_elements = pick_elements()
        cache_elements(selected_elements)
        selection_was_picked = True
    else:
        selected_elements = get_cached_elements()
        if not selected_elements:
            selected_elements = pick_elements()
            cache_elements(selected_elements)
            selection_was_picked = True
except OperationCanceledException:
    OUT = {
        "executed": False,
        "selectionCancelled": True,
        "message": "Selection was cancelled."
    }

if "OUT" not in globals():
    available_parameter_names = get_element_parameter_names(selected_elements)

    try:
        cached_parameter_names = list(_CACHED_SELECTED_PARAMETER_NAMES)
    except Exception:
        cached_parameter_names = []

    if not available_parameter_names:
        OUT = {
            "executed": False,
            "selectionCount": len(selected_elements),
            "message": "No parameters were found on the selected elements."
        }
    else:
        parameter_names = show_parameter_picker(
            available_parameter_names,
            cached_parameter_names,
            filter_text
        )

        if parameter_names is None:
            OUT = {
                "executed": False,
                "selectionCount": len(selected_elements),
                "message": "Parameter selection was cancelled."
            }
        else:
            globals()["_CACHED_SELECTED_PARAMETER_NAMES"] = list(parameter_names)

    if "OUT" not in globals() and not parameter_names:
        OUT = {
            "executed": False,
            "selectionCount": len(selected_elements),
            "availableParameterCount": len(available_parameter_names),
            "message": "Select at least one parameter from the picker."
        }
    elif "OUT" not in globals() and not execute:
        OUT = {
            "executed": False,
            "selectionCount": len(selected_elements),
            "selectionWasPicked": selection_was_picked,
            "availableParameterCount": len(available_parameter_names),
            "parameterNames": parameter_names,
            "message": "Set Execute to true after choosing parameters."
        }
    elif "OUT" not in globals():
        updated = []
        failed = []

        TransactionManager.Instance.EnsureInTransaction(doc)

        try:
            for element in selected_elements:
                element_failures = []
                element_updates = []

                for parameter_name in parameter_names:
                    try:
                        parameter = element.LookupParameter(parameter_name)
                    except Exception:
                        parameter = None

                    ok, detail = clear_parameter_value(parameter)

                    if ok:
                        element_updates.append({
                            "parameter": parameter_name,
                            "detail": detail
                        })
                    else:
                        element_failures.append({
                            "parameter": parameter_name,
                            "reason": detail
                        })

                if element_updates:
                    updated.append({
                        "elementId": element.Id.IntegerValue,
                        "updatedCount": len(element_updates),
                        "updated": element_updates
                    })

                if element_failures:
                    failed.append({
                        "elementId": element.Id.IntegerValue,
                        "failedCount": len(element_failures),
                        "failed": element_failures
                    })
        finally:
            TransactionManager.Instance.TransactionTaskDone()

        OUT = {
            "executed": True,
            "selectionCount": len(selected_elements),
            "parameterCount": len(parameter_names),
            "parameterNames": parameter_names,
            "updatedElementCount": len(updated),
            "failedElementCount": len(failed),
            "updated": updated,
            "failed": failed
        }
