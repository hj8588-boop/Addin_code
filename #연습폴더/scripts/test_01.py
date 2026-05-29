import clr

# Revit / Dynamo references
clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager

clr.AddReference("RevitAPI")
import Autodesk
from Autodesk.Revit.DB import *

clr.AddReference("RevitNodes")
import Revit
clr.ImportExtensions(Revit.Elements)
clr.ImportExtensions(Revit.GeometryConversion)


doc = DocumentManager.Instance.CurrentDBDocument
uiapp = DocumentManager.Instance.CurrentUIApplication
app = uiapp.Application
uidoc = uiapp.ActiveUIDocument


debug = []


def log(message):
    debug.append(str(message))


def to_list(value):
    if value is None:
        return []
    if isinstance(value, list):
        return value
    return [value]


def unwrap_input(value):
    if isinstance(value, list):
        return [UnwrapElement(x) for x in value]
    return UnwrapElement(value)


def mm_to_ft(mm_value):
    if mm_value is None:
        return None
    return float(mm_value) / 304.8


def ft_to_mm(ft_value):
    if ft_value is None:
        return None
    return float(ft_value) * 304.8


# IN
data = IN[0] if len(IN) > 0 else None


# Normalize input
is_list_input = isinstance(data, list)
items = to_list(data)
unwrapped_items = unwrap_input(items)

log("Template started")
log("Input count: {0}".format(len(items)))
log("List input: {0}".format(is_list_input))


# Sample conversion block
sample_mm = 1000.0
sample_ft = mm_to_ft(sample_mm)
log("1000 mm -> {0} ft".format(sample_ft))


# Example result payload
result = {
    "raw_input": data,
    "items": items,
    "unwrapped_items": unwrapped_items,
    "sample_mm": sample_mm,
    "sample_ft": sample_ft,
    "doc_title": doc.Title if doc else None,
    "debug": debug
}


# OUT
OUT = result
