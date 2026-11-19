class_name ComboGrowth
extends RefCounted
## n=C-1；M=(20+5n+n²)/20，保留有理数分子避免小数边界误差。
static func numerator(combo:int)->int:
	var n:int=maxi(combo-1,0)
	return 20+5*n+n*n
static func scale_value(value:int,combo:int)->int:
	return floori(float(value*numerator(combo))/20.0)
static func increase(combo:int,divisor:int=1,base:int=1)->int:
	return floori(float(base*(numerator(combo)-20))/(20.0*divisor))
